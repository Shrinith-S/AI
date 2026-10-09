import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { ToastService } from '../core/app-state';
import { CatalogService } from '../core/catalog.service';
import { errorMessage } from '../core/errors';
import { GenerationStore } from '../core/generation-store';
import { ProjectDetail, Scene, SceneGenerationState, isTerminal } from '../core/models';
import { ExportPanel } from '../shared/export-panel';
import { Icon } from '../shared/icon';

type SceneDraft = Omit<Scene, 'image' | 'video' | 'narrationAudio' | 'imageGeneration' | 'videoGeneration' | 'id' | 'projectId' | 'order'>;

const CAMERAS = ['', 'static', 'push-in', 'pull-back', 'pan-left', 'pan-right', 'tilt-up', 'tilt-down', 'orbit', 'tracking', 'crane-up', 'handheld', 'zoom-in', 'zoom-out'];

@Component({
  selector: 'app-project-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, Icon, DecimalPipe, ExportPanel],
  template: `
    @if (project(); as p) {
      <div class="mx-auto max-w-7xl space-y-6">
        <!-- Header -->
        <div class="flex flex-wrap items-start gap-3">
          <a routerLink="/projects" class="btn-icon mt-1" aria-label="Back to projects"><app-icon name="right" class="rotate-180" /></a>
          <div class="min-w-0 flex-1">
            <h1 class="truncate text-2xl font-bold tracking-tight">{{ p.name }}</h1>
            <p class="text-sm text-muted">
              {{ p.scenes.length }} scenes · {{ totalDuration() | number: '1.0-1' }}s · {{ p.width }}×{{ p.height }} @ {{ p.fps }} fps
              @if (styleName()) { · {{ styleName() }} }
            </p>
          </div>
          <button type="button" class="btn btn-secondary" (click)="toggleSettings()"><app-icon name="sliders" [size]="16" /> Project settings</button>
          <button type="button" class="btn btn-secondary" (click)="addScene()"><app-icon name="plus" [size]="16" /> Add scene</button>
          <button type="button" class="btn btn-ghost hover:text-red-400" (click)="deleteProject()" aria-label="Delete project"><app-icon name="trash" [size]="16" /></button>
        </div>

        @if (p.storyText) {
          <p class="card p-4 text-sm text-muted"><span class="font-medium text-ink">Story:</span> {{ p.storyText }}</p>
        }

        @if (showSettings()) {
          <section class="card grid gap-4 p-5 md:grid-cols-3">
            <div>
              <label class="label" for="pn">Name</label>
              <input id="pn" class="input" [(ngModel)]="settingsDraft.name" />
            </div>
            <div>
              <label class="label" for="ps">Style</label>
              <select id="ps" class="input" [(ngModel)]="settingsDraft.styleId">
                <option [ngValue]="null">None</option>
                @for (s of catalog.styles(); track s.id) { <option [ngValue]="s.id">{{ s.name }}</option> }
              </select>
            </div>
            <div>
              <label class="label" for="pres">Video resolution</label>
              <select id="pres" class="input" [(ngModel)]="settingsDraft.resolution">
                @for (r of ['512x512', '768x432', '1024x576', '432x768', '576x1024', '832x480', '1280x704']; track r) { <option [value]="r">{{ r }}</option> }
              </select>
            </div>
            <div>
              <label class="label" for="pim">Keyframe image model</label>
              <select id="pim" class="input" [(ngModel)]="settingsDraft.imageModelId">
                <option [ngValue]="null">Default</option>
                @for (m of imageModels(); track m.id) { <option [ngValue]="m.id">{{ m.name }}</option> }
              </select>
            </div>
            <div>
              <label class="label" for="pvm">Video model</label>
              <select id="pvm" class="input" [(ngModel)]="settingsDraft.videoModelId">
                <option [ngValue]="null">Default</option>
                @for (m of videoModels(); track m.id) { <option [ngValue]="m.id">{{ m.name }}</option> }
              </select>
            </div>
            <div>
              <label class="label" for="pfps">FPS</label>
              <select id="pfps" class="input" [(ngModel)]="settingsDraft.fps">
                @for (f of [12, 16, 24]; track f) { <option [ngValue]="f">{{ f }}</option> }
              </select>
            </div>
            <div class="md:col-span-3">
              <span class="label">Characters in this project</span>
              <div class="flex flex-wrap gap-1.5">
                @for (c of catalog.characters(); track c.id) {
                  <button type="button" class="chip" [class.chip-active]="settingsDraft.characterIds.includes(c.id)" (click)="toggleProjectCharacter(c.id)">{{ c.name }}</button>
                } @empty {
                  <a routerLink="/characters" class="text-xs text-amber-500">Create a character</a>
                }
              </div>
            </div>
            <div class="flex gap-2 md:col-span-3">
              <button type="button" class="btn btn-primary" (click)="saveSettings()"><app-icon name="check" [size]="16" /> Save</button>
              <button type="button" class="btn btn-ghost" (click)="showSettings.set(false)">Close</button>
            </div>
          </section>
        }

        <!-- Timeline strip -->
        <section class="card p-4">
          <div class="mb-3 flex items-center justify-between text-xs text-muted">
            <span class="font-medium uppercase tracking-wide">Timeline</span>
            <span>Drag to reorder · total {{ totalDuration() | number: '1.0-1' }}s</span>
          </div>
          <div class="flex gap-1.5 overflow-x-auto pb-1">
            @for (s of p.scenes; track s.id; let i = $index) {
              <button type="button" draggable="true" class="group relative h-20 shrink-0 overflow-hidden rounded-md border-2 bg-panel-2 text-left transition"
                [class]="dragOver() === i ? 'border-amber-500' : 'border-line hover:border-muted/60'"
                [style.width.px]="Math.max(80, s.durationSeconds * 28)"
                (dragstart)="dragFrom = i" (dragover)="$event.preventDefault(); dragOver.set(i)" (dragleave)="dragOver.set(null)"
                (drop)="drop(i)" (click)="scrollTo(s.id)">
                @if (s.image?.thumbnailUrl ?? s.image?.url; as thumb) {
                  <img [src]="thumb" class="absolute inset-0 h-full w-full object-cover opacity-70" alt="" />
                }
                <span class="relative block bg-gradient-to-b from-black/70 to-transparent px-2 py-1 text-[11px] font-medium text-white">
                  {{ i + 1 }}{{ s.video ? ' ▶' : '' }} · {{ s.durationSeconds }}s
                </span>
              </button>
            } @empty {
              <p class="text-sm text-muted">No scenes yet.</p>
            }
          </div>
        </section>

        <!-- Scenes -->
        <div class="space-y-4">
          @for (s of p.scenes; track s.id; let i = $index; let last = $last) {
            <article class="card overflow-hidden" [id]="'scene-' + s.id">
              <div class="grid gap-0 md:grid-cols-[minmax(0,22rem)_minmax(0,1fr)]">
                <!-- Media -->
                <div class="relative flex aspect-video items-center justify-center bg-black md:aspect-auto md:min-h-56">
                  @if (s.video) {
                    <video [src]="s.video.url" controls playsinline preload="metadata" [poster]="s.image?.url ?? ''" class="h-full w-full object-contain"></video>
                  } @else if (s.image) {
                    <img [src]="s.image.url" class="h-full w-full object-contain" alt="Scene keyframe" />
                  } @else {
                    <div class="text-center text-xs text-muted"><app-icon name="image" [size]="28" /><p class="mt-1">No keyframe yet</p></div>
                  }
                  @for (state of [live(s.imageGeneration), live(s.videoGeneration)]; track $index) {
                    @if (state && !isTerminal(state.status)) {
                      <div class="absolute inset-x-0 bottom-0 bg-black/75 p-2 text-[11px] text-white">
                        <div class="flex justify-between"><span>{{ state.statusMessage ?? state.status }}</span><span>{{ state.progress }}%</span></div>
                        <div class="mt-1 h-1 overflow-hidden rounded-full bg-white/20"><div class="h-full bg-amber-500 transition-all" [style.width.%]="state.progress"></div></div>
                      </div>
                    } @else if (state?.status === 'Failed') {
                      <div class="absolute inset-x-0 bottom-0 bg-red-950/90 p-2 text-[11px] text-red-200" [title]="state!.errorMessage ?? ''">
                        Failed: {{ state!.errorMessage }}
                      </div>
                    }
                  }
                </div>

                <!-- Details -->
                <div class="space-y-3 p-4">
                  <div class="flex items-start gap-2">
                    <span class="grid h-7 w-7 shrink-0 place-items-center rounded-full bg-panel-2 text-xs font-semibold">{{ i + 1 }}</span>
                    <h2 class="min-w-0 flex-1 font-semibold leading-7">{{ s.title }}</h2>
                    <button type="button" class="btn-icon" [disabled]="i === 0" (click)="move(i, -1)" aria-label="Move up"><app-icon name="up" [size]="16" /></button>
                    <button type="button" class="btn-icon" [disabled]="last" (click)="move(i, 1)" aria-label="Move down"><app-icon name="down" [size]="16" /></button>
                    <button type="button" class="btn-icon" (click)="duplicate(s)" title="Duplicate"><app-icon name="copy" [size]="16" /></button>
                    <button type="button" class="btn-icon hover:text-red-400" (click)="remove(s)" title="Delete"><app-icon name="trash" [size]="16" /></button>
                  </div>

                  @if (editing() === s.id) {
                    <div class="grid gap-3 sm:grid-cols-2">
                      <div class="sm:col-span-2"><label class="label">Title</label><input class="input" [(ngModel)]="draft.title" /></div>
                      <div><label class="label">Location</label><input class="input" [(ngModel)]="draft.location" /></div>
                      <div><label class="label">Mood</label><input class="input" [(ngModel)]="draft.mood" /></div>
                      <div class="sm:col-span-2"><label class="label">Action</label><input class="input" [(ngModel)]="draft.action" /></div>
                      <div class="sm:col-span-2"><label class="label">Image prompt (keyframe)</label><textarea class="input min-h-20" [(ngModel)]="draft.imagePrompt"></textarea></div>
                      <div class="sm:col-span-2"><label class="label">Video prompt (motion)</label><textarea class="input min-h-20" [(ngModel)]="draft.videoPrompt"></textarea></div>
                      <div class="sm:col-span-2"><label class="label">Negative prompt</label><input class="input" [(ngModel)]="draft.negativePrompt" /></div>
                      <div>
                        <label class="label">Camera movement</label>
                        <select class="input" [(ngModel)]="draft.cameraMovement">
                          @for (c of cameras; track c) { <option [ngValue]="c || null">{{ c || 'None' }}</option> }
                        </select>
                      </div>
                      <div><label class="label">Duration (sec)</label><input type="number" min="1" max="20" step="0.5" class="input" [(ngModel)]="draft.durationSeconds" /></div>
                      <div class="sm:col-span-2"><label class="label">Narration / subtitle</label><textarea class="input min-h-16" [(ngModel)]="draft.narration"></textarea></div>
                      <div class="sm:col-span-2">
                        <span class="label">Characters (injected into prompts)</span>
                        <div class="flex flex-wrap gap-1.5">
                          @for (c of catalog.characters(); track c.id) {
                            <button type="button" class="chip" [class.chip-active]="draft.characterIds.includes(c.id)" (click)="toggleDraftCharacter(c.id)">{{ c.name }}</button>
                          }
                        </div>
                      </div>
                      <div class="flex gap-2 sm:col-span-2">
                        <button type="button" class="btn btn-primary btn-sm" (click)="saveScene(s)"><app-icon name="check" [size]="14" /> Save scene</button>
                        <button type="button" class="btn btn-ghost btn-sm" (click)="editing.set(null)">Cancel</button>
                      </div>
                    </div>
                  } @else {
                    <dl class="grid gap-x-4 gap-y-1.5 text-sm sm:grid-cols-[7rem_1fr]">
                      @if (s.location) { <dt class="text-muted">Location</dt><dd>{{ s.location }}</dd> }
                      @if (s.mood) { <dt class="text-muted">Mood</dt><dd>{{ s.mood }}</dd> }
                      <dt class="text-muted">Image prompt</dt><dd class="line-clamp-2">{{ s.imagePrompt || '—' }}</dd>
                      <dt class="text-muted">Video prompt</dt><dd class="line-clamp-2">{{ s.videoPrompt || '—' }}</dd>
                      <dt class="text-muted">Camera</dt><dd>{{ s.cameraMovement || '—' }} · {{ s.durationSeconds }}s</dd>
                      @if (s.narration) { <dt class="text-muted">Narration</dt><dd class="italic">“{{ s.narration }}”</dd> }
                      @if (s.characterIds.length) { <dt class="text-muted">Characters</dt><dd>{{ characterNames(s.characterIds) }}</dd> }
                    </dl>
                    @if (s.narrationAudio) {
                      <audio [src]="s.narrationAudio.url" controls class="h-8 w-full max-w-sm"></audio>
                    }
                  }

                  @if (editing() !== s.id) {
                    <div class="flex flex-wrap gap-2 border-t border-line pt-3">
                      <button type="button" class="btn btn-secondary btn-sm" (click)="startEdit(s)"><app-icon name="edit" [size]="14" /> Edit</button>
                      <button type="button" class="btn btn-secondary btn-sm" [disabled]="busy(s, 'image')" (click)="generate(s, 'image')">
                        <app-icon name="image" [size]="14" /> {{ s.image ? 'Regenerate keyframe' : 'Generate keyframe' }}
                      </button>
                      <button type="button" class="btn btn-primary btn-sm" [disabled]="!s.image || busy(s, 'video')" (click)="generate(s, 'animate')"
                        title="Image-to-video from the keyframe: best character consistency">
                        <app-icon name="film" [size]="14" /> {{ s.video ? 'Regenerate video' : 'Animate keyframe' }}
                      </button>
                      <button type="button" class="btn btn-secondary btn-sm" [disabled]="busy(s, 'video')" (click)="generate(s, 'video')">
                        <app-icon name="video" [size]="14" /> Text to video
                      </button>
                      <button type="button" class="btn btn-secondary btn-sm" [disabled]="!s.narration || narrating() === s.id" (click)="narrate(s)"
                        title="Generate the voice-over locally with Piper/Coqui">
                        <app-icon name="mic" [size]="14" /> {{ narrating() === s.id ? 'Speaking…' : s.narrationAudio ? 'Redo voice-over' : 'Voice-over' }}
                      </button>
                      <div class="ml-auto flex items-center gap-1 text-xs text-muted">
                        <span>Duration</span>
                        <select class="input !w-20 !py-1 text-xs" [ngModel]="s.durationSeconds" (ngModelChange)="setDuration(s, $event)" [attr.aria-label]="'Duration of scene ' + (i + 1)">
                          @for (d of durations(s.durationSeconds); track d) { <option [ngValue]="d">{{ d }}s</option> }
                        </select>
                      </div>
                    </div>
                  }
                </div>
              </div>
            </article>
          }
        </div>

        <app-export-panel [project]="p" (exported)="reload()" />
      </div>
    } @else if (loadError()) {
      <div class="mx-auto max-w-xl p-10 text-center text-sm text-red-400">{{ loadError() }}</div>
    } @else {
      <p class="p-10 text-center text-sm text-muted">Loading project…</p>
    }
  `,
})
export class ProjectDetailPage implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly store = inject(GenerationStore);
  private readonly router = inject(Router);
  protected readonly catalog = inject(CatalogService);

  /** Route parameter. */
  readonly id = input.required<string>();

  protected readonly Math = Math;
  protected readonly isTerminal = isTerminal;
  protected readonly cameras = CAMERAS;
  protected readonly project = signal<ProjectDetail | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly editing = signal<string | null>(null);
  protected readonly narrating = signal<string | null>(null);
  protected readonly showSettings = signal(false);
  protected readonly dragOver = signal<number | null>(null);
  protected dragFrom: number | null = null;
  protected draft: SceneDraft = this.emptyDraft();
  protected settingsDraft = { name: '', styleId: null as string | null, imageModelId: null as string | null, videoModelId: null as string | null, resolution: '768x432', fps: 24, characterIds: [] as string[] };

  protected readonly totalDuration = computed(() => this.project()?.scenes.reduce((sum, s) => sum + s.durationSeconds, 0) ?? 0);
  protected readonly styleName = computed(() => this.catalog.styles().find((s) => s.id === this.project()?.styleId)?.name ?? null);
  protected readonly imageModels = computed(() => this.catalog.models().filter((m) => m.capabilities.includes('TextToImage')));
  protected readonly videoModels = computed(() => this.catalog.models().filter((m) => m.capabilities.includes('TextToVideo') || m.capabilities.includes('ImageToVideo')));

  private unsubscribe?: () => void;
  private reloadTimer?: ReturnType<typeof setTimeout>;

  async ngOnInit(): Promise<void> {
    void this.catalog.ensureLoaded();
    this.unsubscribe = this.store.onUpdate((g) => {
      const p = this.project();
      if (!p || g.projectId !== p.id) return;
      // Refresh scene media once one of this project's generations finishes.
      if (isTerminal(g.status)) {
        clearTimeout(this.reloadTimer);
        this.reloadTimer = setTimeout(() => void this.reload(), 400);
      }
    });
    await this.reload();
  }

  ngOnDestroy(): void {
    this.unsubscribe?.();
    clearTimeout(this.reloadTimer);
  }

  async reload(): Promise<void> {
    try {
      const p = await this.api.project(this.id());
      this.project.set(p);
      this.loadError.set(null);
    } catch (e) {
      this.loadError.set(errorMessage(e));
    }
  }

  /** Latest state of a scene generation: live from SignalR when available. */
  protected live(state: SceneGenerationState | null): SceneGenerationState | null {
    if (!state) return null;
    const g = this.store.get(state.id);
    return g ? { id: g.id, status: g.status, progress: g.progress, statusMessage: g.statusMessage, errorMessage: g.errorMessage } : state;
  }

  protected busy(s: Scene, kind: 'image' | 'video'): boolean {
    const state = this.live(kind === 'image' ? s.imageGeneration : s.videoGeneration);
    return !!state && !isTerminal(state.status);
  }

  protected characterNames(ids: string[]): string {
    return ids.map((id) => this.catalog.characters().find((c) => c.id === id)?.name ?? '?').join(', ');
  }

  protected durations(current: number): number[] {
    return [...new Set([3, 4, 5, 6, 7, 8, 10, current])].sort((a, b) => a - b);
  }

  protected startEdit(s: Scene): void {
    this.draft = {
      title: s.title, description: s.description, location: s.location, action: s.action, mood: s.mood,
      imagePrompt: s.imagePrompt, videoPrompt: s.videoPrompt, negativePrompt: s.negativePrompt, cameraMovement: s.cameraMovement,
      durationSeconds: s.durationSeconds, narration: s.narration, characterIds: [...s.characterIds],
    };
    this.editing.set(s.id);
  }

  protected toggleDraftCharacter(id: string): void {
    const ids = this.draft.characterIds;
    this.draft.characterIds = ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id];
  }

  protected async saveScene(s: Scene): Promise<void> {
    await this.run(async () => {
      await this.api.updateScene(s.id, { ...this.draft, durationSeconds: Number(this.draft.durationSeconds) });
      this.editing.set(null);
      this.toast.success('Scene saved');
    });
  }

  protected async setDuration(s: Scene, seconds: number): Promise<void> {
    await this.run(() => this.api.updateScene(s.id, { ...this.sceneBody(s), durationSeconds: Number(seconds) }));
  }

  protected async generate(s: Scene, kind: 'image' | 'video' | 'animate'): Promise<void> {
    await this.run(async () => {
      const g = await this.api.generateScene(s.id, { kind });
      this.store.upsert(g);
      this.toast.show(kind === 'image' ? 'Keyframe queued' : 'Video queued — this can take a while');
    });
  }

  protected async narrate(s: Scene): Promise<void> {
    this.narrating.set(s.id);
    await this.run(async () => {
      await this.api.sceneNarration(s.id);
      this.toast.success('Voice-over generated');
    });
    this.narrating.set(null);
  }

  protected async duplicate(s: Scene): Promise<void> {
    await this.run(() => this.api.duplicateScene(s.id));
  }

  protected async remove(s: Scene): Promise<void> {
    if (!confirm(`Delete "${s.title}"? Generated media stays in History.`)) return;
    await this.run(() => this.api.deleteScene(s.id));
  }

  protected async move(index: number, delta: number): Promise<void> {
    await this.reorder(index, index + delta);
  }

  protected async drop(target: number): Promise<void> {
    this.dragOver.set(null);
    if (this.dragFrom === null || this.dragFrom === target) return;
    await this.reorder(this.dragFrom, target);
    this.dragFrom = null;
  }

  protected scrollTo(sceneId: string): void {
    document.getElementById(`scene-${sceneId}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  protected async addScene(): Promise<void> {
    const p = this.project();
    if (!p) return;
    await this.run(async () => {
      const scene = await this.api.addScene(p.id, {
        title: `Scene ${p.scenes.length + 1}`,
        imagePrompt: '',
        videoPrompt: '',
        durationSeconds: 5,
        characterIds: p.characters.map((c) => c.id),
      });
      await this.reload();
      this.startEdit(scene);
    }, false);
  }

  protected toggleSettings(): void {
    const p = this.project();
    if (!p) return;
    if (!this.showSettings()) {
      this.settingsDraft = {
        name: p.name,
        styleId: p.styleId,
        imageModelId: p.imageModelId,
        videoModelId: p.videoModelId,
        resolution: `${p.width}x${p.height}`,
        fps: p.fps,
        characterIds: p.characters.map((c) => c.id),
      };
    }
    this.showSettings.update((v) => !v);
  }

  protected toggleProjectCharacter(id: string): void {
    const ids = this.settingsDraft.characterIds;
    this.settingsDraft.characterIds = ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id];
  }

  protected async saveSettings(): Promise<void> {
    const p = this.project();
    if (!p) return;
    const [width, height] = this.settingsDraft.resolution.split('x').map(Number);
    await this.run(async () => {
      await this.api.updateProject(p.id, {
        name: this.settingsDraft.name,
        description: p.description,
        styleId: this.settingsDraft.styleId,
        imageModelId: this.settingsDraft.imageModelId,
        videoModelId: this.settingsDraft.videoModelId,
        width,
        height,
        fps: Number(this.settingsDraft.fps),
        characterIds: this.settingsDraft.characterIds,
      });
      this.showSettings.set(false);
      this.toast.success('Project saved');
    });
  }

  protected async deleteProject(): Promise<void> {
    const p = this.project();
    if (!p || !confirm(`Delete project "${p.name}" and its scenes? Generated media stays in History.`)) return;
    try {
      await this.api.deleteProject(p.id);
      await this.router.navigate(['/projects']);
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  private async reorder(from: number, to: number): Promise<void> {
    const p = this.project();
    if (!p || to < 0 || to >= p.scenes.length) return;
    const ids = p.scenes.map((s) => s.id);
    const [moved] = ids.splice(from, 1);
    ids.splice(to, 0, moved);
    await this.run(() => this.api.reorderScenes(p.id, ids));
  }

  private sceneBody(s: Scene): SceneDraft {
    return {
      title: s.title, description: s.description, location: s.location, action: s.action, mood: s.mood,
      imagePrompt: s.imagePrompt, videoPrompt: s.videoPrompt, negativePrompt: s.negativePrompt, cameraMovement: s.cameraMovement,
      durationSeconds: s.durationSeconds, narration: s.narration, characterIds: s.characterIds,
    };
  }

  private emptyDraft(): SceneDraft {
    return { title: '', description: null, location: null, action: null, mood: null, imagePrompt: '', videoPrompt: '', negativePrompt: null, cameraMovement: null, durationSeconds: 5, narration: null, characterIds: [] };
  }

  private async run(action: () => Promise<unknown>, reload = true): Promise<void> {
    try {
      await action();
      if (reload) await this.reload();
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }
}
