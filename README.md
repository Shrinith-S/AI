# Chitra Studio

A self-hosted AI creative studio for **images, short videos and story videos**, built on free, open-source models that run on your own hardware through [ComfyUI](https://github.com/comfyanonymous/ComfyUI).

> **Local AI Mode — No API charges.** There are no credits, subscriptions or credit card. Your real cost is the hardware and the electricity it uses. GPU inference is never free, and if you point ComfyUI at a rented cloud GPU, that provider bills you.

## What it does

| Feature | Details |
|---|---|
| Text to image | SD 1.5, SDXL, FLUX.1 schnell; aspect presets, batch 1–4, seed/steps/CFG/sampler |
| Text to video | LTX-Video 2B, Wan 2.1 1.3B, Wan 2.2 TI2V 5B, HunyuanVideo; 3/5/10 s, 12/16/24 fps |
| Image to video | Upload or pick an image, add a motion prompt, motion strength and camera movement |
| Prompt enhancer | Free, offline, rule-based; can be swapped for an LLM provider later |
| Characters | Reusable descriptions injected into every prompt for visual consistency; reference image and portrait generation |
| Styles | Built-in and custom prompt wrappers |
| Story video | Synopsis → editable scenes (image prompt, video prompt, camera, duration, narration, characters) |
| Timeline | Reorder (drag or arrows), duplicate, delete, change duration, generate and regenerate per scene |
| Final export | FFmpeg: concatenation, cross-fades, voice-over, burned-in subtitles, background music; MP4 at 480p/720p/1080p |
| Voice-over | Piper (default) or a Coqui TTS server, both local |
| History | Filters (images, videos, stories, failed, completed), search, download, regenerate, variation, delete, use in project |
| Models | Install state read from ComfyUI, VRAM fit (Recommended / Limited / Insufficient), downloads that show exact sizes and need confirmation |
| Live progress | Background queue + SignalR; step-level progress from ComfyUI's WebSocket |
| No local GPU? | Optional **Kaggle provider**: batches jobs into private notebook runs on Kaggle's free T4 GPUs through the official API ([KAGGLE.md](KAGGLE.md)) |
| Honest errors | For example: "ComfyUI is not running. Start ComfyUI at http://localhost:8188 and try again." Nothing is faked. |

## Stack

- **Frontend:** Angular 22 (standalone, signals, zoneless), Tailwind CSS v4, dark mode by default.
- **Backend:** .NET 10 Web API, Clean Architecture (Domain / Application / Infrastructure / Api), EF Core, SignalR.
- **Database:** SQLite out of the box; PostgreSQL optional.
- **AI engine:** ComfyUI behind an `IAiGenerationProvider` abstraction. Models and workflows are JSON files, not code.
- **Media:** local FFmpeg, local Piper TTS, local file storage.

## Quick start (Windows, development)

1. Prerequisites: .NET 10 SDK, Node.js 22+, ComfyUI, FFmpeg. Piper is optional.
2. Start ComfyUI (default `http://localhost:8188`).
3. In this folder, run `.\start-dev.ps1`.
4. Open <http://localhost:4200>. Use **Models** to see what is installed, then go to **Create**.
5. When you're done, run `.\stop-dev.ps1`.

On Linux or macOS, use `./start-dev.sh` and `./stop-dev.sh`. Full Windows + NVIDIA instructions are in **[SETUP.md](SETUP.md)**.

### Docker

```bash
cp .env.example .env
docker compose --profile gpu up -d --build   # LOCAL GPU (NVIDIA Container Toolkit required)
docker compose --profile cpu up -d --build   # CPU only (slow)
docker compose up -d --build                 # REMOTE: set COMFYUI_URL in .env
```

The UI runs at <http://localhost:8080>. See SETUP.md for details.

## Project layout

```
backend/            .NET solution (Chitra.slnx)
  src/Chitra.Domain           entities, enums
  src/Chitra.Application      use cases, interfaces, prompt/story logic
  src/Chitra.Infrastructure   EF Core, ComfyUI, FFmpeg, TTS, storage, background workers
  src/Chitra.Api              controllers, SignalR hub, ai/models.json, ai/workflows/*.json
  tests/Chitra.Tests          unit + integration tests (xUnit)
frontend/           Angular app
docker/comfyui/     optional ComfyUI image
data/, storage/     created at runtime (database, generated media)
```

## Build status

All five planned phases are implemented:

| Phase | Scope | Status |
|---|---|---|
| 1 | Dashboard, API, SQLite, ComfyUI integration, text-to-image, history, local storage | Done |
| 2 | Text-to-video, image-to-video, background queue, SignalR | Done |
| 3 | Story generator, characters, styles, scene management | Done |
| 4 | FFmpeg export, timeline, voice-over, subtitles, music | Done |
| 5 | Authentication (optional JWT/OIDC), projects, model management, advanced settings | Done |

**Verification so far.**
- 52 automated backend tests pass. They drive the real ComfyUI client, provider, workflows and queue against a scripted ComfyUI protocol stub, and run one real FFmpeg export.
- All model download URLs were checked against Hugging Face.
- The Angular production build has no warnings.

**Not yet verified.** A full generation against a real ComfyUI has not been run on the development machine, because it has no NVIDIA GPU.

More documentation:
- [ARCHITECTURE.md](ARCHITECTURE.md): how it fits together, and how to add models, workflows or providers.
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md): common errors and fixes.

## Licenses

Chitra Studio's own code is yours to license. Each model has its own license, shown on the Models page (for example, CreativeML OpenRAIL for Stable Diffusion, Apache-2.0 for FLUX schnell and Wan, and custom licenses for LTX-Video and HunyuanVideo). Read those licenses before any commercial use.
