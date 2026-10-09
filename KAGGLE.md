# Kaggle GPU provider

This guide lets Chitra Studio generate images and video clips on Kaggle's free NVIDIA GPUs. Your laptop doesn't need a GPU: the frontend, backend, history and FFmpeg export all keep running locally.

## How it works (batch mode)

```
Chitra UI → Chitra API → GenerationService → KaggleProvider ──queue──► KaggleRunCoordinator
                                                                         │ kaggle kernels push   (official API)
                                                                         ▼
                                    Kaggle private notebook "chitra-gpu-worker" (GPU T4 ×2)
                                      worker.py: install ComfyUI v0.39.0 → download needed models
                                      → one ComfyUI per GPU (127.0.0.1 only) → run workflows
                                      → /kaggle/working/chitra_outputs/{results.json, files}
                                                                         │ kaggle kernels status / output
                                                                         ▼
                       outputs imported as normal media → History, projects, timeline, FFmpeg export
```

- New jobs are collected for a short **batch window** (30 s by default). They're then sent together as **one** notebook run, so all of them share one setup cost.
- The run script is `backend/src/Chitra.Api/kaggle/worker.py` with the jobs embedded (gzip + base64). The workflows are the same `ai/workflows/*.json` templates, and the prompts are the same ones Chitra builds locally.
- The worker starts **one ComfyUI per GPU**, so the two T4s run two jobs in parallel. **GPU memory is not combined**: each T4 has about 15 GB, and a model that needs more than that is refused with an explanation.
- Chitra polls `kaggle kernels status` every 20 s. When the run ends, it downloads that exact notebook version's output and checks the `runId` inside `results.json`.
- Job and run state is saved in `storage/kaggle/state.json`. Restarting Chitra doesn't lose or duplicate a run.

### Why not a live connection to the notebook?

Exposing ComfyUI from a Kaggle notebook (ngrok or other tunnels, public web UIs) isn't offered by Kaggle. It runs into the Acceptable Use Policy's ban on circumventing the service, and users report automated blocking and account bans for it. Chitra therefore uses only the official API that Kaggle provides for running notebooks. Nothing in the notebook listens on a public interface.

### Honest limitations

| Limitation | Effect in Chitra |
|---|---|
| Each run starts a fresh session | Every run spends **~5–15 min** cloning ComfyUI, installing requirements and downloading model weights before generating. LTX-Video, for example, is 11.2 GB. |
| No connection to a running notebook | No step-by-step progress. Jobs show *Collecting jobs → Kaggle run queued → Running on Kaggle for Xm → Downloaded from Kaggle*. |
| No supported way to stop a run | Cancel removes jobs that haven't been sent yet. A run that has started finishes remotely, and its result is discarded. |
| One active session per notebook | Runs are sequential. Jobs queued during a run go into the next one. |
| Weekly GPU quota (about 30 h, varies; shown in your Kaggle settings) | When it's exhausted, jobs fail with a quota message. Switch to local ComfyUI in Settings until it resets. |
| Session time limit (12 h max; Chitra uses 120 min by default) | Jobs not finished when a session ends fail with "session ended before this job finished". Finished jobs in the same run are kept. |
| Data leaves your machine | Prompts and input images are uploaded to Kaggle. For this reason Local-Only mode blocks Kaggle. |
| Inputs | Image-to-video input images must be 3 MB or smaller (they are embedded in the run script). |

### Model fit on a Tesla T4 (15 GB, no bf16/fp8 compute)

| Model | On Kaggle T4 | Notes |
|---|---|---|
| LTX-Video 2B v0.9.5 | **Recommended first test** | 11.2 GB download. 768×448, 3–5 s clips. |
| Wan 2.1 T2V 1.3B | Good second test | 9.8 GB download. 832×480, up to 81 frames at 16 fps. |
| Wan 2.2 TI2V 5B | Possible, slow | 18 GB download. Offloads to system RAM. |
| SD 1.5 / SDXL / FLUX schnell | Images | FLUX is a 17 GB download per run. |
| HunyuanVideo | **Refused** | Needs at least 16 GB on one GPU; the two T4s can't be combined. |

The T4 has no native bf16 or fp8 compute. ComfyUI converts such weights to fp16 or fp32 automatically, which works but uses more memory and time. None of this has been benchmarked on Kaggle yet; see "Verification status".

## Setup (one time)

1. **Kaggle account.** Phone-verify it at <https://www.kaggle.com/settings>; this is required for GPUs and for internet access in notebooks.
2. **API token.** Go to <https://www.kaggle.com/settings> → *API* → **Generate New Token**, and save the token text to `%USERPROFILE%\.kaggle\access_token`. The older `kaggle.json` file also works. **Never paste the token into Chitra or commit it.**
3. **Kaggle client.** Chitra calls the official `kaggle` Python package. On the development PC it's already installed in `C:\AI\.tools\kaggle`. Elsewhere:
   ```powershell
   python -m venv C:\AI\.tools\kaggle
   C:\AI\.tools\kaggle\Scripts\python.exe -m pip install kaggle
   ```
4. **Settings in Chitra:**
   - Turn **Local-Only** off.
   - Set *AI provider* to **Kaggle free GPU**.
   - Set *Python with the Kaggle client* to `C:\AI\.tools\kaggle\Scripts\python.exe`.
   - Save. The provider status should read "Kaggle batch mode as @yourname on 2× Tesla T4…".
5. **First test.** In Video Generator, choose **LTX-Video 2B**, 3 s, 768×432 (adjusted to 768×448), 24 fps, with a short prompt. Generate, then watch the run at `https://www.kaggle.com/code/<you>/chitra-gpu-worker`.

Chitra creates the private notebook `chitra-gpu-worker` on the first run. Don't edit or run it by hand, because Chitra would then download the wrong version's output; it detects this and reports it. To use a different notebook, change *Notebook slug*.

### Docker

The backend image includes Python and the Kaggle client. To use your token in the container, mount the folder that holds it (read-only) by uncommenting the `KAGGLE_CONFIG_DIR` volume in `docker-compose.yml`, then set `KAGGLE_CONFIG_DIR` in `.env`.

## Costs

- **Money:** none. Kaggle notebooks with GPU T4 ×2 are free, Chitra uses no paid API, and model weights are public Hugging Face downloads.
- **GPU quota:**
  - Each run uses roughly *setup (~3 min for LTX-Video) + generation time* from your weekly quota.
  - A single 3 s LTX-Video clip **measured ~8 GPU-minutes** in total (see below).
  - Batching amortizes setup: four clips in one run cost one setup, not four.
  - With a ~30 h quota, that's on the order of 70–120 short single-clip runs per week, and more with batching.
- **Your laptop:** only network traffic. Outputs are tens of MB per clip; the model downloads happen on Kaggle, not on your connection.

## Security

- Credentials stay in `~/.kaggle`. Chitra calls `kaggle/bridge.py`, which runs the official client and prints JSON only, never the token. Neither the token nor local paths are written into the run script; an automated test checks this.
- The notebook is created **private** (`is_private: true`).
- ComfyUI inside the notebook listens on `127.0.0.1` only. Nothing is exposed, and Chitra is not a proxy to the notebook.
- Outputs are downloaded only for the notebook version Chitra pushed, and the run id inside `results.json` must match.
- Kaggle runs automated content moderation. Keep prompts within Kaggle's community guidelines; unsafe content can get the account suspended.

## Troubleshooting

| Message | Fix |
|---|---|
| "Kaggle is not ready: Kaggle credentials not found or invalid…" | Create the token (Setup step 2) and check the file location. |
| "Python was not found at …" / "The 'kaggle' Python package is not installed…" | Fix *Python with the Kaggle client* in Settings (Setup step 3). |
| "…runs on remote servers… Turn off Local-Only" | Local-Only blocks Kaggle by design. |
| "Kaggle GPU quota is exhausted…" | Wait for the weekly reset (shown in your Kaggle settings), or switch the provider back to ComfyUI. |
| "The Kaggle run failed… See the log at …" | Open the notebook link. Chitra also saves the log tail to `storage/kaggle/runs/<run>/output/kaggle-log.txt`. |
| "…needs at least N GB… not combined across the two GPUs" | Use a smaller model (LTX-Video 2B, Wan 1.3B) or a lower resolution. |
| A job stays on "Kaggle run queued" | Kaggle is waiting for a free GPU. It's normal at busy times. |

## Measured on Kaggle (2026-10-10, GPU T4 ×2, account @shrinithsanil)

**Test:** LTX-Video 2B, text-to-video, 768×448, 73 frames at 24 fps (3.04 s), 30 steps, seed 2026.

**Kaggle environment:**
- 2× Tesla T4, 14,911 MB each, compute capability 7.5.
- Preinstalled torch 2.11.0+cu128.
- ComfyUI v0.39.0 printed a "pytorch with cu130 for optimized CUDA operations" warning. It's only a warning; generation works.

**Timings:**

| Stage | Time |
|---|---|
| Queued → GPU session running | ~40 s |
| Setup: clone, pip install, download 11.2 GB at ~100 MB/s | ~2.5 min |
| Generation (306 s on one T4) | ~5 min |
| **Click → clip in Chitra** | **~8 min 50 s** |
| Quota used | **~8 GPU-minutes** |

**Result:** an H.264 MP4, 601 KB, which then exported through FFmpeg to 720p with subtitles in 4 s.

**Bug found by the first real run (fixed):** the requirements filter also skipped `torchsde`, so ComfyUI failed at startup. Chitra reported the failure, including ComfyUI's startup log, as a failed generation.

## Verification status

**Verified on the development PC (no Kaggle account involved):**
- Official CLI 2.2.4 flags (`push --accelerator/--timeout`, `status`, `output --file-pattern`), the status values, and that no cancel is exposed were all checked against the client's source.
- `worker.py` ran for real against local ComfyUI v0.39.0 (CPU) and generated an SD 1.5 image. Its outputs, `results.json`, the missing-model failure and cleanup all behaved correctly.
- A real C#-generated `run.py` was executed by `worker.py` on local ComfyUI, and the generation completed in Chitra (test `RealWorkerScript_RunsGeneratedPayload_OnLocalComfyUi`).
- Automated tests with a scripted Kaggle API cover:
  - batching
  - image-to-video input embedding
  - quota exhausted, out of memory, and a session killed mid-run
  - Local-Only blocking
  - VRAM refusal for HunyuanVideo
  - no secrets in the pushed script
  - Kaggle clips going through the real FFmpeg export

**Verified on real Kaggle:**
- Push, status polling and output download.
- GPU detection (2× T4).
- ComfyUI v0.39.0 installed on Kaggle's PyTorch.
- LTX-Video 2B loading and generating a 3 s clip.
- Output import and FFmpeg export.
- A clear error report when ComfyUI fails to start.
- The token never appears in uploaded files or logs.

**Still unverified on Kaggle:**
- Wan 2.1 / Wan 2.2.
- Image-to-video.
- Two jobs running in parallel on both GPUs.
- Quota exhaustion and a session hitting its time limit. These are covered only by simulated tests.
