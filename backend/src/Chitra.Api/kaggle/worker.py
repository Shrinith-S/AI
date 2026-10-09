#!/usr/bin/env python3
"""
Chitra Studio - Kaggle GPU batch worker.

Chitra pushes this script to Kaggle as a private "script" kernel with the job payload embedded below, using the
official Kaggle API (kaggle kernels push). Kaggle runs it top to bottom on a GPU session. The script:

  1. reports the GPUs (CUDA devices, VRAM),
  2. installs a pinned ComfyUI on top of Kaggle's preinstalled CUDA PyTorch,
  3. downloads only the model files the queued jobs need (public Hugging Face URLs),
  4. starts one ComfyUI per GPU (two T4s = two jobs in parallel; GPU memory is never pooled),
  5. runs each job's ComfyUI workflow and copies outputs to /kaggle/working/chitra_outputs,
  6. writes chitra_outputs/results.json, which Chitra downloads with "kaggle kernels output".

No credentials are used or needed here. Nothing listens on a public interface: ComfyUI binds to 127.0.0.1 inside
the session and is never exposed.

Local testing (no Kaggle): set CHITRA_COMFYUI_DIR to an existing ComfyUI checkout, CHITRA_COMFYUI_PYTHON to its
python, CHITRA_ALLOW_CPU=1, CHITRA_OUT/CHITRA_WORK to temp folders and CHITRA_PAYLOAD_FILE to a payload file.
"""

import base64
import gzip
import json
import os
import queue
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import traceback
import urllib.error
import urllib.request
from pathlib import Path

PAYLOAD = "__CHITRA_PAYLOAD__"

WORKER_VERSION = "1.0.0"
COMFYUI_REPO = "https://github.com/comfyanonymous/ComfyUI.git"
COMFYUI_REF = "v0.39.0"  # Version Chitra's workflow templates were validated against.

OUT = Path(os.environ.get("CHITRA_OUT", "/kaggle/working/chitra_outputs"))
WORK = Path(os.environ.get("CHITRA_WORK", os.path.join(tempfile.gettempdir(), "chitra")))  # not saved as output
LOCAL_COMFY = os.environ.get("CHITRA_COMFYUI_DIR")
LOCAL_COMFY_PYTHON = os.environ.get("CHITRA_COMFYUI_PYTHON")
ALLOW_CPU = os.environ.get("CHITRA_ALLOW_CPU") == "1"
BASE_PORT = int(os.environ.get("CHITRA_BASE_PORT", "8188"))
POLL_SECONDS = 3

RESULTS: dict = {}
RESULTS_LOCK = threading.Lock()


def log(message: str) -> None:
    print(f"[chitra {time.strftime('%H:%M:%S')}] {message}", flush=True)


def now() -> str:
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def save_results() -> None:
    """Atomically rewrites results.json so partial progress is visible even if a later job fails."""
    with RESULTS_LOCK:
        OUT.mkdir(parents=True, exist_ok=True)
        tmp = OUT / "results.json.tmp"
        tmp.write_text(json.dumps(RESULTS, indent=2), encoding="utf-8")
        os.replace(tmp, OUT / "results.json")


def set_job(key: str, **fields) -> None:
    with RESULTS_LOCK:
        RESULTS["jobs"][key].update(fields)
    save_results()


def load_payload() -> dict:
    if os.environ.get("CHITRA_PAYLOAD_FILE"):
        data = Path(os.environ["CHITRA_PAYLOAD_FILE"]).read_text(encoding="utf-8").strip()
    else:
        data = PAYLOAD
    if data.startswith("__CHITRA"):
        raise RuntimeError("No job payload: this worker template must be pushed by Chitra Studio.")
    return json.loads(gzip.decompress(base64.b64decode(data)).decode("utf-8"))


# ---------------------------------------------------------------- GPU


def gpu_report() -> list:
    try:
        smi = subprocess.run(
            ["nvidia-smi", "--query-gpu=index,name,memory.total,memory.free,driver_version", "--format=csv,noheader"],
            capture_output=True, text=True, timeout=30,
        )
        log("nvidia-smi:\n" + (smi.stdout.strip() or smi.stderr.strip()))
    except (OSError, subprocess.SubprocessError) as e:
        log(f"nvidia-smi not available: {e}")

    gpus = []
    try:
        import torch  # Kaggle's preinstalled CUDA build

        log(f"torch {torch.__version__}, CUDA available: {torch.cuda.is_available()}, CUDA runtime: {torch.version.cuda}")
        for i in range(torch.cuda.device_count()):
            free, total = torch.cuda.mem_get_info(i)
            props = torch.cuda.get_device_properties(i)
            gpus.append({
                "index": i,
                "name": props.name,
                "totalMb": total // (1024 * 1024),
                "freeMb": free // (1024 * 1024),
                "computeCapability": f"{props.major}.{props.minor}",
            })
            log(f"GPU {i}: {props.name}, {total / 1024**3:.1f} GB total, {free / 1024**3:.1f} GB free")
    except Exception as e:  # torch missing or broken
        log(f"Could not query CUDA through torch: {e}")
    return gpus


# ---------------------------------------------------------------- ComfyUI install


def install_comfyui() -> tuple:
    if LOCAL_COMFY:
        log(f"Using existing ComfyUI at {LOCAL_COMFY}")
        return Path(LOCAL_COMFY), LOCAL_COMFY_PYTHON or sys.executable

    comfy = WORK / "ComfyUI"
    if not (comfy / "main.py").exists():
        log(f"Cloning ComfyUI {COMFYUI_REF}")
        WORK.mkdir(parents=True, exist_ok=True)
        subprocess.run(["git", "clone", "--depth", "1", "--branch", COMFYUI_REF, COMFYUI_REPO, str(comfy)], check=True)

    # Keep Kaggle's CUDA-enabled torch; installing torch from PyPI could replace it with a mismatched build.
    # Match exact package names only: "torchsde" and similar ComfyUI dependencies must still be installed.
    requirements = (comfy / "requirements.txt").read_text(encoding="utf-8").splitlines()
    kept = [r for r in requirements if r.strip() and not r.strip().startswith("#")
            and package_name(r) not in ("torch", "torchvision", "torchaudio")]
    filtered = WORK / "requirements.chitra.txt"
    filtered.write_text("\n".join(kept), encoding="utf-8")
    log("Installing ComfyUI requirements (keeping the preinstalled CUDA torch)")
    subprocess.run([sys.executable, "-m", "pip", "install", "-q", "-r", str(filtered)], check=True)
    return comfy, sys.executable


def package_name(requirement: str) -> str:
    """'torchsde>=0.2' -> 'torchsde'; 'Pillow' -> 'pillow'."""
    name = requirement.strip()
    for separator in ("==", ">=", "<=", "~=", "!=", ">", "<", "[", ";", " ", "@"):
        name = name.split(separator, 1)[0]
    return name.strip().lower()


# ---------------------------------------------------------------- models


def download(url: str, dest: Path, expected: int) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    part = dest.with_suffix(dest.suffix + ".part")
    for attempt in range(1, 4):
        start = part.stat().st_size if part.exists() else 0
        request = urllib.request.Request(url, headers={"User-Agent": "ChitraStudio-KaggleWorker/1.0"})
        if start:
            request.add_header("Range", f"bytes={start}-")
        try:
            with urllib.request.urlopen(request, timeout=120) as response, open(part, "ab" if start else "wb") as f:
                if start and response.status != 206:
                    f.truncate(0)
                    start = 0
                done, last = start, -1
                while True:
                    chunk = response.read(8 * 1024 * 1024)
                    if not chunk:
                        break
                    f.write(chunk)
                    done += len(chunk)
                    pct = int(done * 100 / expected) if expected else 0
                    if pct // 10 != last:
                        last = pct // 10
                        log(f"  {dest.name}: {done / 1024**3:.2f} GB ({pct}%)")
            size = part.stat().st_size
            if expected and size != expected:
                raise IOError(f"size mismatch: got {size} bytes, expected {expected}")
            os.replace(part, dest)
            return
        except (urllib.error.URLError, IOError, TimeoutError) as e:
            log(f"  download attempt {attempt} failed for {dest.name}: {e}")
            if attempt == 3:
                raise
            time.sleep(5 * attempt)


def ensure_models(comfy: Path, jobs: list) -> dict:
    """Downloads each needed file once. Returns {fileName: error} for files that could not be fetched."""
    needed = {}
    for job in jobs:
        for f in job["files"]:
            needed[(f["folder"], f["fileName"])] = f
    errors = {}
    for (folder, name), f in needed.items():
        dest = comfy / "models" / folder / name
        expected = int(f.get("sizeBytes") or 0)
        if dest.exists() and (not expected or dest.stat().st_size == expected):
            log(f"Model file present: {folder}/{name}")
            continue
        if not f.get("url"):
            errors[name] = f"{name} has no download URL and is not present in models/{folder}."
            continue
        log(f"Downloading {folder}/{name} ({expected / 1024**3:.2f} GB)")
        try:
            download(f["url"], dest, expected)
        except Exception as e:
            errors[name] = f"Could not download {name}: {e}"
    return errors


# ---------------------------------------------------------------- ComfyUI servers


def http_json(url: str, body=None, timeout=30):
    data = None if body is None else json.dumps(body).encode("utf-8")
    request = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        text = response.read().decode("utf-8")
        return json.loads(text) if text else {}


def start_server(comfy: Path, python: str, index: int, use_cpu: bool) -> tuple:
    port = BASE_PORT + index
    log_file = open(WORK / f"comfyui_{index}.log", "w", encoding="utf-8")
    args = [python, "main.py", "--listen", "127.0.0.1", "--port", str(port), "--disable-auto-launch"]
    args += ["--cpu"] if use_cpu else ["--cuda-device", str(index)]
    process = subprocess.Popen(args, cwd=str(comfy), stdout=log_file, stderr=subprocess.STDOUT)
    deadline = time.time() + 600
    while time.time() < deadline:
        if process.poll() is not None:
            log_text = tail(WORK / f"comfyui_{index}.log", 15)
            cause = next((line for line in reversed(log_text.splitlines()) if "Error" in line or "Exception" in line), "see log")
            raise RuntimeError(f"ComfyUI {index} exited during startup: {cause.strip()}\n{log_text}")
        try:
            stats = http_json(f"http://127.0.0.1:{port}/system_stats", timeout=5)
            log(f"ComfyUI {index} ready on port {port} ({stats.get('system', {}).get('comfyui_version')}, "
                f"{', '.join(d.get('name', '?') for d in stats.get('devices', []))})")
            return process, port
        except (urllib.error.URLError, ConnectionError, TimeoutError, OSError):
            time.sleep(2)
    process.kill()
    raise RuntimeError(f"ComfyUI {index} did not start within 10 minutes:\n{tail(WORK / f'comfyui_{index}.log')}")


def tail(path: Path, lines: int = 40) -> str:
    try:
        return "\n".join(path.read_text(encoding="utf-8", errors="replace").splitlines()[-lines:])
    except OSError:
        return ""


# ---------------------------------------------------------------- jobs


def describe_rejection(body: str) -> tuple:
    try:
        data = json.loads(body)
    except ValueError:
        return "InvalidWorkflow", f"ComfyUI rejected the workflow: {body[:500]}"
    details = []
    for node_id, node in (data.get("node_errors") or {}).items():
        for e in node.get("errors", []):
            detail = e.get("details") or e.get("message", "")
            if e.get("type") == "value_not_in_list" and detail.split(":")[0] in ("ckpt_name", "unet_name", "clip_name", "clip_name1", "clip_name2", "vae_name"):
                return "ModelMissing", f"Model file missing on the Kaggle worker ({node.get('class_type')}): {detail}"
            details.append(f"{node.get('class_type')} #{node_id}: {detail}")
    message = (data.get("error") or {}).get("message", "validation failed")
    return "InvalidWorkflow", f"ComfyUI rejected the workflow: {message}. {'; '.join(details[:5])}"


def run_job(job: dict, comfy: Path, port: int, gpu_index: int) -> None:
    key = job["key"]
    started = time.time()
    set_job(key, status="running", startedAt=now(), gpu=gpu_index)
    log(f"Job {key} ({job['type']}, model {job['modelId']}) on GPU {gpu_index}")

    for item in job.get("inputs", []):
        (comfy / "input" / item["name"]).write_bytes(base64.b64decode(item["dataB64"]))

    try:
        response = http_json(f"http://127.0.0.1:{port}/prompt", {"prompt": job["workflow"], "client_id": "chitra-kaggle"})
        prompt_id = response["prompt_id"]
    except urllib.error.HTTPError as e:
        code, message = describe_rejection(e.read().decode("utf-8", errors="replace"))
        set_job(key, status="failed", errorCode=code, error=message, finishedAt=now())
        return

    deadline = started + int(job.get("timeoutSeconds") or 3600)
    while True:
        time.sleep(POLL_SECONDS)
        history = http_json(f"http://127.0.0.1:{port}/history/{prompt_id}").get(prompt_id)
        if history:
            break
        if time.time() > deadline:
            try:
                http_json(f"http://127.0.0.1:{port}/interrupt", {"prompt_id": prompt_id})
            except Exception:
                pass
            set_job(key, status="failed", errorCode="Timeout", finishedAt=now(),
                    error=f"Job exceeded {job.get('timeoutSeconds')} s on the Kaggle GPU.")
            return

    status = history.get("status", {})
    if status.get("status_str") == "error":
        error = next((m[1] for m in status.get("messages", []) if m and m[0] == "execution_error"), {})
        message = str(error.get("exception_message", "unknown error")).strip()
        exc_type = str(error.get("exception_type", ""))
        oom = "OutOfMemory" in exc_type or "out of memory" in message.lower() or "Allocation on device" in message
        set_job(key, status="failed", finishedAt=now(),
                errorCode="InsufficientVram" if oom else "GenerationFailed",
                error=(f"The Kaggle GPU ran out of memory in {error.get('node_type')}. Lower the resolution or frame count."
                       if oom else f"ComfyUI failed in {error.get('node_type')}: {message[:600]}"))
        return

    outputs = []
    job_dir = OUT / key
    job_dir.mkdir(parents=True, exist_ok=True)
    for node in (history.get("outputs") or {}).values():
        for value in node.values():
            if not isinstance(value, list):
                continue
            for item in value:
                if not isinstance(item, dict) or "filename" not in item or item.get("type", "output") != "output":
                    continue
                source = comfy / "output" / item.get("subfolder", "") / item["filename"]
                if source.exists():
                    shutil.copy2(source, job_dir / item["filename"])
                    outputs.append(f"{key}/{item['filename']}")
                    source.unlink()  # keep the session's disk tidy

    if not outputs:
        set_job(key, status="failed", errorCode="InvalidWorkflow", finishedAt=now(),
                error="ComfyUI finished but produced no output files.")
        return

    set_job(key, status="completed", outputs=outputs, finishedAt=now(), seconds=round(time.time() - started, 1))
    log(f"Job {key} completed in {time.time() - started:.0f} s: {', '.join(outputs)}")


def main() -> None:
    payload = load_payload()
    jobs = payload["jobs"]
    RESULTS.update({
        "runId": payload["runId"],
        "workerVersion": WORKER_VERSION,
        "comfyuiRef": COMFYUI_REF,
        "startedAt": now(),
        "gpus": [],
        "error": None,
        "jobs": {j["key"]: {"status": "pending"} for j in jobs},
    })
    save_results()
    log(f"Run {payload['runId']}: {len(jobs)} job(s)")

    servers = []
    try:
        gpus = gpu_report()
        RESULTS["gpus"] = gpus
        save_results()
        if not gpus and not ALLOW_CPU:
            raise RuntimeError("No CUDA GPU in this Kaggle session. Make sure the GPU accelerator was granted "
                               "(weekly GPU quota may be exhausted).")

        WORK.mkdir(parents=True, exist_ok=True)
        comfy, python = install_comfyui()

        download_errors = ensure_models(comfy, jobs)
        runnable = []
        for job in jobs:
            missing = [download_errors[f["fileName"]] for f in job["files"] if f["fileName"] in download_errors]
            if missing:
                set_job(job["key"], status="failed", errorCode="ModelMissing", error="; ".join(missing), finishedAt=now())
            else:
                runnable.append(job)

        if runnable:
            count = max(1, min(len(gpus), len(runnable))) if gpus else 1
            for i in range(count):
                servers.append(start_server(comfy, python, i, use_cpu=not gpus))

            work_queue = queue.Queue()
            for job in runnable:
                work_queue.put(job)

            def worker(gpu_index: int, port: int) -> None:
                while True:
                    try:
                        job = work_queue.get_nowait()
                    except queue.Empty:
                        return
                    try:
                        run_job(job, comfy, port, gpu_index)
                    except Exception as e:  # keep the other jobs going
                        set_job(job["key"], status="failed", errorCode="GenerationFailed", finishedAt=now(),
                                error=f"Worker error: {e}")
                        traceback.print_exc()

            threads = [threading.Thread(target=worker, args=(i, port)) for i, (_, port) in enumerate(servers)]
            for t in threads:
                t.start()
            for t in threads:
                t.join()
    except Exception as e:
        traceback.print_exc()
        RESULTS["error"] = str(e)[:2000]
        for key, job in RESULTS["jobs"].items():
            if job.get("status") in ("pending", "running"):
                job.update(status="failed", errorCode="GenerationFailed", error=f"Kaggle worker failed: {str(e)[:600]}")
    finally:
        for process, _ in servers:
            process.terminate()
        if not LOCAL_COMFY:
            for job in jobs:
                for item in job.get("inputs", []):
                    try:
                        (WORK / "ComfyUI" / "input" / item["name"]).unlink()
                    except OSError:
                        pass
        RESULTS["finishedAt"] = now()
        save_results()
        done = sum(1 for j in RESULTS["jobs"].values() if j.get("status") == "completed")
        log(f"Finished: {done}/{len(jobs)} job(s) completed. Results in {OUT / 'results.json'}")


if __name__ == "__main__":
    main()
    # Always exit 0 so Kaggle keeps /kaggle/working; job-level failures are reported in results.json.
    sys.exit(0)
