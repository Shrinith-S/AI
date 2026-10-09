# Setup

This guide covers a native development setup on **Windows 10/11 with an NVIDIA GPU**, which is the recommended path. Docker setup and other platforms follow at the end.

## 1. Check your hardware

| GPU memory (VRAM) | What works well |
|---|---|
| none / CPU only | SD 1.5 images (minutes per image). Video is not practical. |
| 6–8 GB | SD 1.5, SDXL (with `--lowvram`), LTX-Video and Wan 1.3B at low resolution and short clips |
| 12 GB | SDXL, FLUX schnell fp8, LTX-Video, Wan 2.1 1.3B, Wan 2.2 5B (with offloading) |
| 16–24 GB+ | All included models, including HunyuanVideo |

You also need:
- 32 GB of system RAM, which is recommended for video models.
- 20–80 GB of free disk space for models.

After setup, **Settings → System information** shows what the app detected.

## 2. NVIDIA driver

Install a current Game Ready or Studio driver from <https://www.nvidia.com/drivers>. Then check that it works:

```powershell
nvidia-smi
```

That command should list your GPU and its driver version. Chitra uses `nvidia-smi` to detect VRAM.

## 3. ComfyUI (the AI engine)

The easiest option is the **portable Windows build**:

1. Download `ComfyUI_windows_portable_nvidia.7z` from <https://github.com/comfyanonymous/ComfyUI/releases>.
2. Extract it to a short path, for example `C:\ComfyUI_windows_portable`.
3. Run `run_nvidia_gpu.bat`. ComfyUI starts at <http://127.0.0.1:8188>.
   - On 6–8 GB GPUs, edit the .bat file and add `--lowvram` to the end of the python line.

Use a **2025 or newer** ComfyUI. The video workflows rely on the core nodes `CreateVideo`, `SaveVideo`, `LTXVImgToVideo` and `Wan22ImageToVideoLatent`. To update, run `update\update_comfyui.bat`.

No custom nodes are required for the included models.

## 4. Models

You have two options for installing models.

**Option A: from the app.**
1. In **Settings**, set *ComfyUI models folder* to `C:\ComfyUI_windows_portable\ComfyUI\models`.
2. Open **Models**, choose **Download…**, review the exact sizes and confirm. Downloads resume if they are interrupted.

**Option B: manually.** Download the files listed on the Models page and copy them into the matching folders:

```
ComfyUI\models\checkpoints\        sd_xl_base_1.0.safetensors, v1-5-pruned-emaonly-fp16.safetensors,
                                   flux1-schnell-fp8.safetensors, ltx-video-2b-v0.9.5.safetensors
ComfyUI\models\diffusion_models\   wan2.1_t2v_1.3B_fp16.safetensors, wan2.2_ti2v_5B_fp16.safetensors, hunyuan_video_t2v_720p_bf16.safetensors
ComfyUI\models\text_encoders\      t5xxl_fp8_e4m3fn.safetensors, umt5_xxl_fp8_e4m3fn_scaled.safetensors, clip_l.safetensors, llava_llama3_fp8_scaled.safetensors
ComfyUI\models\vae\                wan_2.1_vae.safetensors, wan2.2_vae.safetensors, hunyuan_video_vae_bf16.safetensors
```

Files in subfolders, such as `checkpoints\SDXL\…`, are found automatically.

**Good starting set** (about 17 GB):
- SDXL (images).
- LTX-Video 2B with the T5 fp8 encoder (text-to-video and image-to-video).

## 5. FFmpeg (thumbnails, "use as next scene", final export)

```powershell
winget install Gyan.FFmpeg
```

Then open a **new** terminal and check that it works:

```powershell
ffmpeg -version
```

If FFmpeg isn't on your PATH, set its full path in **Settings → FFmpeg path**.

## 6. Piper TTS (optional, for voice-over)

1. Download `piper_windows_amd64.zip` from <https://github.com/rhasspy/piper/releases> and extract it, for example to `C:\Tools\piper`.
2. Download a voice: both the `.onnx` file and its matching `.onnx.json`. For example, `en_US-lessac-medium` from <https://huggingface.co/rhasspy/piper-voices>. Put both files in a folder such as `C:\Tools\piper\voices`.
3. In **Settings**:
   - Piper executable: `C:\Tools\piper\piper.exe`
   - Voices folder: `C:\Tools\piper\voices`
   - Default voice: `en_US-lessac-medium`

## 7. .NET and Node

- .NET 10 SDK: <https://dotnet.microsoft.com/download>
- Node.js 22 or newer: <https://nodejs.org>

## 8. Run Chitra Studio

```powershell
cd C:\AI
.\start-dev.ps1        # builds and starts the API on :5080 and the UI on :4200
```

Open <http://localhost:4200>. The top bar shows **Local AI Mode — No API charges**, and the sidebar shows whether ComfyUI is connected.

To stop everything:

```powershell
.\stop-dev.ps1
```

Data locations:
- Database: `data\chitra.db`
- Generated media: `storage\` (`images`, `videos`, `audio`, `projects`, `thumbnails`, `uploads`)

To change these paths, edit `backend\src\Chitra.Api\appsettings.json` (`Database:SqlitePath`, `Storage:RootPath`) or set environment variables such as `Storage__RootPath=D:\ChitraMedia`.

## 9. Run the tests

```powershell
cd backend
dotnet test
```

The real-FFmpeg export test runs only when `ffmpeg` is on your PATH.

---

## Docker

Install Docker Desktop. For GPU containers, you also need the NVIDIA Container Toolkit; on Windows that means WSL2 with GPU support. Then:

```bash
cp .env.example .env
```

| Mode | Command | Notes |
|---|---|---|
| LOCAL GPU | `docker compose --profile gpu up -d --build` | ComfyUI in a CUDA container. Models go in `./comfyui/models` (`COMFYUI_MODELS_DIR`). |
| CPU | `docker compose --profile cpu up -d --build` | Works anywhere, but only SD 1.5 is practical. |
| REMOTE | `docker compose up -d --build` | Set `COMFYUI_URL`. On Windows, running ComfyUI natively and using `http://host.docker.internal:8188` avoids GPU passthrough setup. |
| PostgreSQL | add `--profile postgres` and set `DB_PROVIDER=Postgres` | Migrations run automatically on start. |

After it starts:
- The UI is at <http://localhost:8080> and the API at <http://localhost:5080>.
- The backend image includes FFmpeg.

**Remote ComfyUI and Local-Only mode.** With Local-Only mode on, Chitra refuses ComfyUI URLs outside loopback and private networks, because remote GPU servers usually cost money. Turn Local-Only off in Settings if that's intentional.

## Authentication (optional)

By default there is a single local user and no login. To use Microsoft Entra ID, Google or any other OpenID Connect provider, set these values in `appsettings.json` or as environment variables:

```json
"Auth": { "Mode": "Jwt", "Authority": "https://login.microsoftonline.com/<tenant>/v2.0", "Audience": "api://<app-id>" }
```

The API then requires bearer tokens, and users are created on first sign-in. The bundled UI has no login screen yet. Add one with an OIDC library such as `angular-auth-oidc-client` and attach the token, or put the app behind an authenticating reverse proxy.

## Windows without an NVIDIA GPU (CPU mode)

Images with SD 1.5 work, at roughly 20 s per sampling step on a laptop CPU (about 7 minutes per 512×512 image at 20 steps). Video models are not practical. This is the setup used on the development machine:

```powershell
git clone https://github.com/comfyanonymous/ComfyUI.git C:\ComfyUI
cd C:\ComfyUI
git checkout v0.39.0
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cpu
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe main.py --cpu --listen 127.0.0.1 --port 8188   # or double-click run_cpu.bat
```

Then finish in Chitra:
1. Set **Settings → ComfyUI models folder** to `C:\ComfyUI\models`, and the default image model to *Stable Diffusion 1.5*.
2. Download SD 1.5 from the **Models** page.

## Linux / macOS

1. Install ComfyUI from source following its README.
2. Install FFmpeg (`apt install ffmpeg` or `brew install ffmpeg`), Piper, .NET 10 and Node.
3. Run `./start-dev.sh`.

Apple Silicon works through ComfyUI's MPS backend. It's much slower than an NVIDIA GPU for video.
