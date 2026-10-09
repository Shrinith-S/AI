# Architecture

## Overview

```
 Angular (4200 dev / nginx 8080)
   │  REST /api/*            │  SignalR /hubs/studio (generationUpdated, exportUpdated, modelDownloadUpdated)
   ▼                         ▼
 Chitra.Api ── controllers, StudioHub, ProblemDetails error mapping, optional JWT
   │
 Chitra.Application ── use cases (GenerationService, SceneService, StoryService, ExportService, …)
   │                    interfaces: IAiGenerationProvider, IModelCatalog, ITtsProvider, IPromptEnhancer,
   │                                IStoryPlanner, IFfmpegService, IMediaStorage, IGenerationQueue, …
   ▼
 Chitra.Infrastructure ── EF Core (SQLite/PostgreSQL), ComfyUI client + provider + WebSocket tracker,
                          FFmpeg, Piper/Coqui, local storage, GenerationWorker, ExportWorker, model downloads
   │
   ├── ComfyUI (local or LAN) ── /prompt /history /view /queue /system_stats /object_info /upload/image /ws
   ├── ffmpeg / ffprobe (local)
   └── piper (local) or Coqui tts-server (local)
```

`Chitra.Domain` holds the entities and depends on nothing else. `Chitra.Application` depends only on Domain and on EF Core abstractions, through `IAppDbContext`.

## Generation flow

1. `POST /api/generations/image` (or `/video`, or `/image-to-video`) reaches `GenerationService.CreateAsync`, which:
   1. validates input and resolves the model from `ai/models.json`;
   2. gets the provider through `IAiProviderRegistry`, which enforces Local-Only mode;
   3. fails fast with a clear message if ComfyUI is unreachable or model files are missing (it checks `/object_info`);
   4. builds the final prompt with `PromptComposer`: optional enhancement, then characters injected inline (`Ramu (65-year-old man, …) walks…`), then camera and motion wording, then the style;
   5. fits the resolution and frame count to the model (multiples of 8/16/32; frames of the form `8n+1` or `4n+1`; model maximums), recording any adjustment as a warning;
   6. saves a `Generation` and a `GenerationJob`, then enqueues the job. **The HTTP call returns `202` immediately.**
2. `GenerationWorker`, a hosted service, dequeues jobs while respecting *Max concurrent generations*. For each job, `GenerationProcessor`:
   1. calls `provider.Generate*Async`; `ComfyUiProvider` renders the workflow template and calls `POST /prompt` with its WebSocket `client_id`;
   2. polls `GetGenerationStatusAsync` every second, using live WebSocket progress where available and confirming completion through `/history`;
   3. pushes progress through SignalR, with throttled DB writes;
   4. on completion, downloads each output via `/view` into `storage/`, creates `MediaAsset`s (probing with FFmpeg and generating thumbnails), and links scene outputs to their `Scene`;
   5. maps errors to codes: `InsufficientVram` (CUDA out of memory), `ModelMissing`, `InvalidWorkflow`, `Timeout`, `Cancelled`, `GenerationFailed`.
3. On restart, unfinished jobs are re-queued. Jobs that already have a ComfyUI `prompt_id` are **resumed**, not resubmitted.

Cancellation removes a pending prompt from ComfyUI's queue, or interrupts it if it is running.

## Adding a model or workflow (no code changes)

1. Build the workflow in ComfyUI, then save it in API format (*Workflow → Export (API)*).
2. Replace the values with placeholders and save it in `backend/src/Chitra.Api/ai/workflows/`. Use the exact string `"{{name}}"` to get a typed value (number or bool); placeholders inside longer strings are substituted as text.

   | Placeholder | Meaning |
   |---|---|
   | `{{prompt}}`, `{{negative_prompt}}` | final prompts |
   | `{{seed}}`, `{{steps}}`, `{{cfg}}`, `{{sampler}}`, `{{scheduler}}`, `{{denoise}}` | sampling |
   | `{{width}}`, `{{height}}`, `{{batch_size}}` | size |
   | `{{frames}}`, `{{fps}}`, `{{frame_rate}}`, `{{motion_strength}}`, `{{shift}}` | video |
   | `{{image}}` | input image uploaded to ComfyUI (image-to-video) |
   | `{{filename_prefix}}` | output name prefix |
   | `{{file.<role>}}` | installed file name for the model file with that `role` |

   Top-level keys that start with `_` (for example `_comment`) are stripped before submission. An unknown placeholder fails with a clear `InvalidWorkflow` error.
3. Add an entry to `ai/models.json`: capabilities, workflow per capability, files (role, folder, size, download URL), VRAM requirements and defaults (`frameMultiple`, `dimensionMultiple`, `maxFrames`, `nativeFps`, …).
4. Restart the API. `WorkflowTests.AllCatalogWorkflows_RenderAndAreWellFormed` checks every catalog workflow: placeholders resolve, node links point to existing nodes, and a Save node is present.

**CogVideoX** and other models that need ComfyUI custom nodes (for example kijai's *ComfyUI-CogVideoXWrapper*) are added the same way. Install the custom nodes, export the workflow, and list the node pack in `requiredCustomNodes` so users see it on the Models page.

## Execution providers

`IAiProviderRegistry.GetForModel` picks the provider for each job:
- A model's `provider` in `models.json` names its workflow engine, which is "ComfyUI" for all included models.
- When *Settings → AI provider* is **Kaggle**, `KaggleProvider` runs those same ComfyUI workflows remotely in batch mode. See [KAGGLE.md](KAGGLE.md).
- `ComfyWorkflowBuilder` renders workflows identically for both providers.

Providers declare what they can do through `IAiGenerationProvider`:

| Member | Meaning |
|---|---|
| `IsLocal` / `IncursApiCharges` | Used by Local-Only enforcement. |
| `MaxJobDuration` | Per-provider job time limit. |
| `ManagesOwnQueue` | Batch jobs bypass the local concurrency limit, so they can share a run. |
| `CanCancelRunningJobs` | Whether a job that has started can be stopped. |
| `DownloadsModelsOnDemand` | Models page shows "Downloaded per Kaggle run". |

## Adding a provider

- **Image/video engine** (for example the Automatic1111/Forge API, or a hosted API): implement `IAiGenerationProvider` in Infrastructure and register it in `AddInfrastructure`. Then set `"provider"` on models in `models.json` to its `Name`. Set `IncursApiCharges = true` for paid APIs; `AiProviderRegistry` blocks those while Local-Only is on.
- **Prompt enhancer:** implement `IPromptEnhancer` (OpenAI, Claude, Gemini or a local LLM) and register it. Users pick it with `Studio:PromptEnhancer`.
- **Story planner:** implement `IStoryPlanner`. An LLM planner can fill the same `ScenePlan` records.
- **TTS:** implement `ITtsProvider` and register it; it then appears as a choice in Settings.

## Data model

| Entity | Purpose |
|---|---|
| `User` | the local user (fixed id) or an external identity (JWT `sub`/`oid`) |
| `Generation` | the request (user prompt, final prompt, parameters) and its result (status, progress, outputs, errors, warnings) |
| `GenerationJob` | one execution attempt on a provider (`ProviderJobId` = ComfyUI `prompt_id`) |
| `MediaAsset` | metadata for a file in `storage/`; the database never holds file contents |
| `Project` | groups characters, scenes, assets and the final video |
| `Scene` | one timeline shot: prompts, camera, duration, narration, characters, chosen image, video and narration |
| `Character`, `Style`, `PromptTemplate` | reusable creative building blocks |
| `VideoExport` | an FFmpeg render request and its result |
| `ProviderConfiguration`, `AppSetting` | provider metadata and user settings, which override `appsettings.json` |

Each provider has its own migrations history:
- SQLite: `Persistence/Migrations/Sqlite`
- PostgreSQL: `Persistence/Migrations/Postgres`

To add a migration:

```bash
dotnet ef migrations add <Name> --context SqliteAppDbContext   --project src/Chitra.Infrastructure --startup-project src/Chitra.Infrastructure --output-dir Persistence/Migrations/Sqlite
dotnet ef migrations add <Name> --context PostgresAppDbContext --project src/Chitra.Infrastructure --startup-project src/Chitra.Infrastructure --output-dir Persistence/Migrations/Postgres
```

## Story planner (rule-based)

`RuleBasedStoryPlanner` works in these steps:
1. Splits the synopsis into clauses on sentence ends and narrative connectors (*but, then, eventually, finally, however…*).
2. Fills in missing subjects ("becomes arrogant" becomes "Ramu becomes arrogant") and maps the protagonist ("a poor old man") to the first selected character.
3. Adds an establishing opening and a closing shot. When more scenes are requested than there are beats, it adds close-up reaction shots; when fewer, it merges beats.
4. Derives location, mood, lighting, camera movement and narration from keyword tables.

The output is a draft for the user to edit.

## Final export (FFmpeg)

`ExportProcessor` builds a `VideoCompositionRequest`, and `FfmpegService.ComposeAsync` then:
1. **Normalizes each scene.** It scales and pads to the target size and fps, holds the last frame if the clip is shorter than the scene, and turns stills into clips. Each scene gets either its narration track or silence, and is stretched if the narration is longer.
2. **Joins the scenes.** It uses the concat demuxer for hard cuts, or `xfade` + `acrossfade` for transitions.
3. **Adds the extras** in a single filter graph: background music (`amix`, looped) and burned-in subtitles (`subtitles` filter, SRT timed to the cut points).

Progress is parsed from `-progress pipe:1`. Every process runs with argument lists and never through a shell.

## API

| Area | Endpoints |
|---|---|
| Generations | `POST /api/generations/image`, `/video`, `/image-to-video`; `GET /api/generations[?filter=images\|videos\|stories\|failed\|completed\|processing&search=]`; `GET/DELETE /api/generations/{id}`; `POST …/{id}/cancel`, `…/regenerate`, `…/variation` |
| Media | `POST /api/media/upload`; `GET /api/media`, `/{id}`, `/{id}/content` (range requests), `/{id}/download`, `/{id}/thumbnail`; `POST /{id}/last-frame`; `DELETE /{id}` |
| Projects | `GET/POST /api/projects`; `GET/PUT/DELETE /api/projects/{id}`; `POST/GET /{id}/assets`; `POST /{id}/scenes`; `POST /{id}/scenes/reorder`; `POST/GET /{id}/exports` |
| Scenes | `PUT/DELETE /api/scenes/{id}`; `POST /{id}/duplicate`, `/{id}/generate` (`image`\|`video`\|`animate`), `/{id}/narration`; `PUT /{id}/media` |
| Stories | `POST /api/stories/generate` |
| Library | `/api/characters`, `/api/styles`, `/api/prompt-templates` (CRUD) |
| Models | `GET /api/models[?capability=]`, `/{id}`, `/{id}/download-plan`; `POST /{id}/download` (`{"confirm":true}` required), `/{id}/download/cancel` |
| System | `GET /api/system/status`, `/api/system/info`, `/api/settings`; `PUT /api/settings`; `GET /api/health` |
| Other | `POST /api/prompts/enhance`, `GET /api/tts/status`, `POST /api/tts/synthesize`, `GET /api/exports/{id}`, `POST /api/exports/{id}/cancel` |

Errors are RFC 7807 problem documents that include a `code`: `ProviderUnavailable` 503, `ModelMissing` 422, `InsufficientVram` 422, `InvalidWorkflow` 422, `FfmpegUnavailable` 503, `TtsUnavailable` 503, `DiskFull` 507, `Timeout` 504, `LocalOnlyViolation` 403, `Validation` 400, `NotFound` 404.

In development, OpenAPI is served at `/openapi/v1.json`.
