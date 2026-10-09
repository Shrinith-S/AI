# Troubleshooting

Each error in the UI comes with a code, also visible in the API response. Look up the message below.

## "ComfyUI is not running. Start ComfyUI at http://localhost:8188 and try again."

- Start ComfyUI (`run_nvidia_gpu.bat`) and wait for "To see the GUI go to: http://127.0.0.1:8188".
- Check the URL in **Settings → ComfyUI URL**, then press **Test**.
- **Docker:** inside the backend container, `localhost` is the container itself. Use `http://comfyui:8188` (gpu/cpu profile) or `http://host.docker.internal:8188` (ComfyUI on the host).
- **ComfyUI on another PC:** start it with `--listen 0.0.0.0` and allow port 8188 through the firewall.

## "… is a paid/remote API and Local-Only mode is on" / "ComfyUI is configured at … which is not on this machine or a private network"

Local-Only mode blocks anything that might cost money. Either use a local or LAN ComfyUI URL, or turn off Local-Only in Settings if you knowingly use a rented GPU server.

## Model not installed (`ModelMissing`)

"SDXL 1.0 Base is not fully installed in ComfyUI. Missing: sd_xl_base_1.0.safetensors (models/checkpoints)."

- Download the model from the **Models** page, or copy the file into that ComfyUI folder.
- ComfyUI rescans folders on request. If the file still isn't found, restart ComfyUI.
- Check the extension: `.safetensors`, not `.safetensors.part` or `.crdownload`.

## Not enough GPU memory (`InsufficientVram`)

"The GPU ran out of memory (VRAM)…"

- Lower the resolution, the duration/FPS (fewer frames) or the number of images.
- Choose a smaller model, for example LTX-Video 2B or Wan 1.3B instead of HunyuanVideo.
- Start ComfyUI with `--lowvram`, or `--novram` as a last resort.
- Close other GPU-heavy apps such as games, browsers with hardware acceleration, or other AI tools.

## Invalid workflow (`InvalidWorkflow`)

- "Cannot execute because node CreateVideo does not exist" means ComfyUI is too old. Update it (`update\update_comfyui.bat`).
- "Workflow uses the placeholder {{x}}…" means a custom workflow uses an unknown placeholder. See ARCHITECTURE.md for the supported ones.
- A model that needs custom nodes (listed on its Models card) needs those nodes installed in ComfyUI.

## Generation timed out

Video models on modest GPUs can take 10–60 minutes. Raise **Settings → Video timeout**, or reduce the frames, steps or resolution. The ComfyUI console shows the actual speed (it/s).

## Progress stays at a few percent, then jumps to 100%

The WebSocket progress stream isn't connected. The usual cause is a reverse proxy without WebSocket support between the API and ComfyUI. Generation still completes, because completion is detected through `/history`.

## "Live updates reconnecting" in the top bar

The browser lost its SignalR connection to the API; the UI polls active jobs meanwhile. With nginx, make sure `/hubs/` is proxied with the `Upgrade` and `Connection` headers (see `frontend/nginx.conf`).

## FFmpeg unavailable

"FFmpeg was not found ('ffmpeg')…"

- Install it with `winget install Gyan.FFmpeg`, then **open a new terminal and restart the API** so it sees the new PATH.
- Or set the full path, for example `C:\ffmpeg\bin\ffmpeg.exe`, in Settings.
- Without FFmpeg, generation still works, but thumbnails, "Use as next scene" and final export do not.
- "This FFmpeg build has no subtitle support (libass)" means you need a full build, such as Gyan's full build.

## Voice-over: "Piper was not found" or "no voices were found"

- Set the Piper executable and voices folder in Settings.
- Each voice needs both `name.onnx` and `name.onnx.json` in that folder.

## Export says scenes have no video or keyframe

Every scene needs at least a keyframe image. Scenes without video are exported as still shots for their duration.

## Disk full (`DiskFull`)

Generation and uploads stop when less than `Storage:MinFreeSpaceMb` (default 1 GB) is free. To free space, delete old items in History (this removes their files) or move `Storage:RootPath` to a bigger drive.

## Model download failed

- Downloads resume from the `.part` file when you start them again.
- "requires accepting its license" means the model is gated. Accept the license on Hugging Face, download it in your browser, and copy it into the shown folder.

## Kaggle provider

See the troubleshooting table in [KAGGLE.md](KAGGLE.md#troubleshooting).

## Reset everything

1. Stop the app with `.\stop-dev.ps1`.
2. Delete `data\chitra.db*` (the database) and, if you want, `storage\` (all generated media).

The database is recreated on the next start.

## Logs

- Dev scripts: `.dev\logs\api.log`, `.dev\logs\web.log`
- Docker: `docker compose logs -f backend`
- ComfyUI: its console window, which shows the real Python error behind any `GenerationFailed`.
