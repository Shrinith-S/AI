import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { StatusService, ToastService } from '../core/app-state';
import { CatalogService } from '../core/catalog.service';
import { errorCode, errorMessage } from '../core/errors';
import { GenerationStore } from '../core/generation-store';
import { Generation, GenerationType, MediaAsset } from '../core/models';
import { GenerationActions } from '../shared/generation-actions';
import { GenerationCard } from '../shared/generation-card';
import { Icon } from '../shared/icon';
import { ImagePicker } from '../shared/image-picker';
import { ModelSelect } from '../shared/model-select';
import { PromptField } from '../shared/prompt-field';
import { StyleCharacterFields } from '../shared/style-character-fields';

type Aspect = '16:9' | '9:16' | '1:1';

/** Spec resolutions, with their portrait counterparts when the aspect ratio is flipped. */
const RESOLUTIONS: Record<Aspect, string[]> = {
  '16:9': ['768x432', '1024x576'],
  '9:16': ['432x768', '576x1024'],
  '1:1': ['512x512', '768x768'],
};

const CAMERA_LABELS: Record<string, string> = {
  static: 'Static',
  'push-in': 'Slow push in',
  'pull-back': 'Pull back',
  'pan-left': 'Pan left',
  'pan-right': 'Pan right',
  'tilt-up': 'Tilt up',
  'tilt-down': 'Tilt down',
  orbit: 'Orbit',
  tracking: 'Tracking shot',
  'crane-up': 'Crane up',
  handheld: 'Handheld',
  'zoom-in': 'Zoom in',
  'zoom-out': 'Zoom out',
};

@Component({
  selector: 'app-video-generator',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, Icon, PromptField, ModelSelect, StyleCharacterFields, ImagePicker, GenerationCard],
  template: `
    <div class="mx-auto grid max-w-7xl gap-6 lg:grid-cols-[minmax(0,26rem)_minmax(0,1fr)]">
      <!-- LEFT: controls -->
      <form class="card h-fit space-y-5 p-5" (ngSubmit)="generate()">
        <div>
          <h1 class="text-xl font-semibold">{{ isI2v() ? 'Image to Video' : 'Video Generator' }}</h1>
          <p class="text-sm text-muted">{{ isI2v() ? 'Animate a still image with a motion prompt.' : 'Short AI video from a text prompt.' }}</p>
        </div>

        <div>
          <span class="label">Generation type</span>
          <div class="seg">
            <button type="button" [class.on]="!isI2v()" (click)="setType('TextToVideo')">Text to Video</button>
            <button type="button" [class.on]="isI2v()" (click)="setType('ImageToVideo')">Image to Video</button>
          </div>
        </div>

        @if (isI2v()) {
          <div>
            <span class="label">Input image</span>
            <app-image-picker [value]="inputAsset()" (changed)="inputAsset.set($event)" />
          </div>
        }

        <app-prompt-field [(value)]="prompt" [type]="type()" [styleHint]="styleName()" [onSubmit]="submitFn"
          [label]="isI2v() ? 'Motion prompt' : 'Prompt'"
          [placeholder]="isI2v() ? 'Describe the motion, e.g. the character slowly walks forward while leaves move in the wind' : 'Describe the scene, action and camera…'" />

        <div>
          <label class="label" for="vneg">Negative prompt</label>
          <textarea id="vneg" name="vneg" class="input min-h-16" [(ngModel)]="negative" placeholder="Things to avoid (model default when empty)"></textarea>
        </div>

        <app-model-select [models]="models()" [(value)]="modelId" />

        <div class="grid grid-cols-3 gap-3">
          <div>
            <span class="label">Duration</span>
            <select class="input" name="dur" [(ngModel)]="duration">
              @for (d of [3, 5, 10]; track d) { <option [ngValue]="d">{{ d }} sec</option> }
            </select>
          </div>
          <div>
            <span class="label">Resolution</span>
            <select class="input" name="res" [(ngModel)]="resolution">
              @for (r of resolutions(); track r) { <option [value]="r">{{ r }}</option> }
            </select>
          </div>
          <div>
            <span class="label">FPS</span>
            <select class="input" name="fps" [(ngModel)]="fps">
              @for (f of [12, 16, 24]; track f) { <option [ngValue]="f">{{ f }}</option> }
            </select>
          </div>
        </div>
        <p class="-mt-3 text-[11px] text-muted">
          ≈ {{ frameEstimate() }} frames
          @if (selectedModel(); as m) {
            · {{ m.name }} max {{ m.defaults.maxFrames }} frames, trained at {{ m.defaults.nativeFps }} fps
          }
        </p>

        <div>
          <label class="label" for="vseed">Seed</label>
          <div class="flex gap-2">
            <input id="vseed" name="vseed" type="number" class="input" [disabled]="randomSeed()" [(ngModel)]="seed" placeholder="Random" />
            <label class="flex items-center gap-1.5 whitespace-nowrap text-sm">
              <input type="checkbox" name="vrs" [(ngModel)]="randomSeed" class="h-4 w-4 accent-amber-500" /> Random
            </label>
          </div>
        </div>

        <details class="rounded-lg border border-line p-3">
          <summary class="cursor-pointer text-sm font-medium">Advanced settings</summary>
          <div class="mt-4 space-y-4">
            <div class="grid grid-cols-2 gap-3">
              <div>
                <label class="label" for="vcfg">CFG scale · {{ cfg() }}</label>
                <input id="vcfg" name="vcfg" type="range" min="1" max="15" step="0.5" class="w-full" [(ngModel)]="cfg" />
              </div>
              <div>
                <label class="label" for="vsteps">Steps · {{ steps() }}</label>
                <input id="vsteps" name="vsteps" type="range" min="4" max="60" class="w-full" [(ngModel)]="steps" />
              </div>
            </div>
            <div>
              <label class="label" for="motion">Motion strength · {{ motionLabel() }}</label>
              <input id="motion" name="motion" type="range" min="0" max="1" step="0.05" class="w-full" [(ngModel)]="motion" />
            </div>
            <div>
              <label class="label" for="cam">Camera movement</label>
              <select id="cam" name="cam" class="input" [(ngModel)]="camera">
                <option value="">None (let the model decide)</option>
                @for (c of cameraOptions(); track c) { <option [value]="c">{{ cameraLabel(c) }}</option> }
              </select>
            </div>
            <div>
              <span class="label">Aspect ratio</span>
              <div class="seg">
                @for (a of aspects; track a) {
                  <button type="button" [class.on]="aspect() === a" (click)="setAspect(a)">{{ a }}</button>
                }
              </div>
            </div>
            <div class="grid gap-4">
              <app-style-character-fields [(styleId)]="styleId" [(characterIds)]="characterIds" />
            </div>
            <label class="flex items-center gap-2 text-sm">
              <input type="checkbox" name="venh" [(ngModel)]="autoEnhance" class="h-4 w-4 accent-amber-500" /> Auto-enhance prompt
            </label>
            <p class="text-[11px] text-muted">Open video models have no native camera or motion controls; these settings are expressed in the prompt.</p>
          </div>
        </details>

        @if (error(); as e) {
          <div class="rounded-lg border border-red-500/30 bg-red-500/10 p-3 text-sm text-red-400" role="alert">
            <p>{{ e.message }}</p>
            @if (e.code === 'ProviderUnavailable') {
              <a routerLink="/settings" class="mt-1 inline-block text-xs underline">Check the ComfyUI URL in Settings</a>
            } @else if (e.code === 'ModelMissing') {
              <a routerLink="/models" class="mt-1 inline-block text-xs underline">Open Models</a>
            }
          </div>
        }

        <button type="submit" class="btn btn-primary w-full py-2.5" [disabled]="!canSubmit()">
          <app-icon name="video" [size]="16" /> {{ submitting() ? 'Queuing…' : 'Generate Video' }}
        </button>
      </form>

      <!-- RIGHT: preview -->
      <section class="min-w-0 space-y-4">
        <div class="card flex min-h-[24rem] items-center justify-center overflow-hidden bg-black/40">
          @if (latest(); as g) {
            @if (video(); as v) {
              <video [src]="v.url" controls autoplay loop playsinline class="max-h-[70vh] w-full bg-black"></video>
            } @else if (g.status === 'Failed') {
              <div class="max-w-md p-8 text-center text-red-400">
                <app-icon name="alert" [size]="28" />
                <p class="mt-2 text-sm">{{ g.errorMessage }}</p>
              </div>
            } @else if (g.status === 'Cancelled') {
              <p class="text-sm text-muted">Cancelled.</p>
            } @else {
              <div class="w-full max-w-sm p-8 text-center">
                <div class="mx-auto h-10 w-10 animate-spin rounded-full border-2 border-amber-500/30 border-t-amber-500"></div>
                <p class="mt-4 text-sm">{{ g.statusMessage ?? g.status }}</p>
                <div class="mt-3 h-2 overflow-hidden rounded-full bg-line">
                  <div class="h-full rounded-full bg-gradient-to-r from-amber-500 to-rose-500 transition-all" [style.width.%]="g.progress"></div>
                </div>
                <p class="mt-2 text-xs text-muted">{{ g.progress }}% · video can take several minutes; you can leave this page</p>
                <button type="button" class="btn btn-ghost btn-sm mt-3" (click)="actions.cancel(g)"><app-icon name="stop" [size]="14" /> Cancel</button>
              </div>
            }
          } @else {
            <div class="p-10 text-center text-muted">
              <app-icon name="video" [size]="36" />
              <p class="mt-3 text-sm">Generate Video</p>
              <p class="mt-1 text-xs">Your clip will play here when it is ready.</p>
            </div>
          }
        </div>

        @if (latest(); as g) {
          @if (video(); as v) {
            <div class="flex flex-wrap gap-2">
              <a class="btn btn-secondary btn-sm" [href]="v.downloadUrl" download><app-icon name="download" [size]="14" /> Download</a>
              <button type="button" class="btn btn-secondary btn-sm" (click)="track(actions.regenerate(g))"><app-icon name="refresh" [size]="14" /> Regenerate</button>
              <button type="button" class="btn btn-secondary btn-sm" (click)="track(actions.variation(g))"><app-icon name="shuffle" [size]="14" /> Create variation</button>
              <button type="button" class="btn btn-secondary btn-sm" [disabled]="extracting()" (click)="useAsNextScene(v)"
                title="Continue from the last frame with image-to-video">
                <app-icon name="right" [size]="14" /> {{ extracting() ? 'Extracting frame…' : 'Use as next scene' }}
              </button>
              <button type="button" class="btn btn-secondary btn-sm" (click)="actions.useInProject(g)"><app-icon name="folder" [size]="14" /> Use in project</button>
              <span class="ml-auto self-center font-mono text-xs text-muted">seed {{ g.seed }} · {{ g.frameCount }}f @ {{ g.fps }}fps</span>
            </div>
          }
          @if (g.warnings.length) {
            <div class="rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-xs text-amber-500">
              @for (w of g.warnings; track w) { <p>{{ w }}</p> }
            </div>
          }
        }

        <div>
          <h2 class="mb-3 text-sm font-semibold text-muted">Recent videos</h2>
          <div class="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4">
            @for (g of history(); track g.id) {
              <app-generation-card [generation]="g" (view)="actions.view($event)" (regenerate)="track(actions.regenerate($event))"
                (variation)="track(actions.variation($event))" (cancel)="actions.cancel($event)" (remove)="actions.remove($event)"
                (useInProject)="actions.useInProject($event)" />
            }
          </div>
        </div>
      </section>
    </div>
  `,
})
export class VideoGeneratorPage implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly store = inject(GenerationStore);
  private readonly status = inject(StatusService);
  private readonly toast = inject(ToastService);
  protected readonly catalog = inject(CatalogService);
  protected readonly actions = inject(GenerationActions);

  /** Bound from route data. */
  readonly mode = input<'t2v' | 'i2v'>('t2v');
  /** Bound from the ?asset= query parameter. */
  readonly asset = input<string | undefined>(undefined);

  protected readonly aspects: Aspect[] = ['16:9', '9:16', '1:1'];
  protected readonly type = signal<GenerationType>('TextToVideo');
  protected readonly isI2v = computed(() => this.type() === 'ImageToVideo');
  protected readonly prompt = signal('');
  protected readonly negative = signal('');
  protected readonly modelId = signal('');
  protected readonly duration = signal(5);
  protected readonly aspect = signal<Aspect>('16:9');
  protected readonly resolution = signal('768x432');
  protected readonly fps = signal(24);
  protected readonly seed = signal<number | null>(null);
  protected readonly randomSeed = signal(true);
  protected readonly cfg = signal(3);
  protected readonly steps = signal(30);
  protected readonly motion = signal(0.5);
  protected readonly camera = signal('');
  protected readonly styleId = signal<string | null>(null);
  protected readonly characterIds = signal<string[]>([]);
  protected readonly autoEnhance = signal(false);
  protected readonly inputAsset = signal<MediaAsset | null>(null);
  protected readonly cameraOptions = signal<string[]>(Object.keys(CAMERA_LABELS));

  protected readonly submitting = signal(false);
  protected readonly extracting = signal(false);
  protected readonly error = signal<{ message: string; code: string | null } | null>(null);
  protected readonly latestId = signal<string | null>(null);
  protected readonly latest = this.store.watch(this.latestId);
  protected readonly history = signal<Generation[]>([]);

  protected readonly models = computed(() => this.catalog.models().filter((m) => m.capabilities.includes(this.type())));
  protected readonly selectedModel = computed(() => this.models().find((m) => m.id === this.modelId()) ?? null);
  protected readonly resolutions = computed(() => RESOLUTIONS[this.aspect()]);
  protected readonly styleName = computed(() => this.catalog.styles().find((s) => s.id === this.styleId())?.name ?? null);
  protected readonly video = computed(() => this.latest()?.outputs.find((o) => o.type === 'Video') ?? null);
  protected readonly frameEstimate = computed(() => Math.round(Number(this.duration()) * Number(this.fps())) + 1);
  protected readonly motionLabel = computed(() => {
    const m = Number(this.motion());
    return m < 0.2 ? 'almost still' : m < 0.45 ? 'gentle' : m < 0.7 ? 'natural' : m < 0.9 ? 'dynamic' : 'energetic';
  });
  protected readonly canSubmit = computed(
    () => !!this.prompt().trim() && !this.submitting() && (!this.isI2v() || !!this.inputAsset()),
  );
  protected readonly submitFn = () => void this.generate();
  private unsubscribe?: () => void;
  private defaultModels = { t2v: '', i2v: '' };

  constructor() {
    effect(() => {
      const model = this.selectedModel();
      if (!model) return;
      untracked(() => {
        this.cfg.set(model.defaults.cfg);
        this.steps.set(model.defaults.steps);
      });
    });

    // Keep the model valid for the current generation type.
    effect(() => {
      const models = this.models();
      const type = this.type();
      untracked(() => {
        if (models.some((m) => m.id === this.modelId())) return;
        const preferred = type === 'ImageToVideo' ? this.defaultModels.i2v : this.defaultModels.t2v;
        this.modelId.set(models.find((m) => m.id === preferred)?.id ?? models.find((m) => m.installState === 'Installed')?.id ?? models[0]?.id ?? '');
      });
    });
  }

  async ngOnInit(): Promise<void> {
    this.type.set(this.mode() === 'i2v' || this.asset() ? 'ImageToVideo' : 'TextToVideo');
    this.unsubscribe = this.store.onUpdate((g) => {
      if (g.type === 'TextToImage') return;
      this.history.update((list) => (list.some((x) => x.id === g.id) ? list.map((x) => (x.id === g.id ? g : x)) : [g, ...list]));
    });

    const assetId = this.asset();
    if (assetId) {
      this.api.asset(assetId).then((a) => this.inputAsset.set(a)).catch((e) => this.toast.error(errorMessage(e)));
    }

    const settings = await this.api.settings().catch(() => null);
    if (settings) {
      this.defaultModels = { t2v: settings.settings.defaultVideoModel, i2v: settings.settings.defaultImageToVideoModel };
      this.fps.set([12, 16, 24].includes(settings.settings.defaultFps) ? settings.settings.defaultFps : 24);
      const res = settings.settings.defaultVideoResolution;
      const aspect = (Object.keys(RESOLUTIONS) as Aspect[]).find((a) => RESOLUTIONS[a].includes(res));
      if (aspect) {
        this.aspect.set(aspect);
        this.resolution.set(res);
      }
    }
    this.modelId.set('');
    await this.catalog.ensureLoaded();
    this.modelId.set(
      this.models().find((m) => m.id === (this.isI2v() ? this.defaultModels.i2v : this.defaultModels.t2v))?.id ?? this.models()[0]?.id ?? '',
    );

    try {
      const page = await this.api.generations({ filter: 'videos', pageSize: 12 });
      page.items.forEach((g) => this.store.upsert(g));
      this.history.set(page.items);
    } catch {
      // Shell shows connectivity problems.
    }
  }

  ngOnDestroy(): void {
    this.unsubscribe?.();
  }

  protected setType(type: GenerationType): void {
    this.type.set(type);
    this.error.set(null);
  }

  protected setAspect(aspect: Aspect): void {
    const index = Math.max(0, RESOLUTIONS[this.aspect()].indexOf(this.resolution()));
    this.aspect.set(aspect);
    this.resolution.set(RESOLUTIONS[aspect][index] ?? RESOLUTIONS[aspect][0]);
  }

  protected cameraLabel(key: string): string {
    return CAMERA_LABELS[key] ?? key;
  }

  protected async generate(): Promise<void> {
    if (!this.canSubmit()) return;
    this.submitting.set(true);
    this.error.set(null);
    const [width, height] = this.resolution().split('x').map(Number);
    const request = {
      prompt: this.prompt(),
      negativePrompt: this.negative() || null,
      modelId: this.modelId() || null,
      durationSeconds: Number(this.duration()),
      width,
      height,
      fps: Number(this.fps()),
      steps: Number(this.steps()),
      cfgScale: Number(this.cfg()),
      seed: this.randomSeed() || this.seed() === null ? null : Number(this.seed()),
      motionStrength: Number(this.motion()),
      cameraMovement: this.camera() || null,
      styleId: this.styleId(),
      characterIds: this.characterIds(),
      enhancePrompt: this.autoEnhance(),
      inputAssetId: this.isI2v() ? (this.inputAsset()?.id ?? null) : null,
    };
    try {
      const g = this.isI2v() ? await this.api.createImageToVideo(request) : await this.api.createVideo(request);
      this.store.upsert(g);
      this.latestId.set(g.id);
      void this.status.refresh();
    } catch (e) {
      this.error.set({ message: errorMessage(e), code: errorCode(e) });
    } finally {
      this.submitting.set(false);
    }
  }

  /** Extracts the final frame (FFmpeg) and switches to image-to-video so the next shot continues seamlessly. */
  protected async useAsNextScene(video: MediaAsset): Promise<void> {
    this.extracting.set(true);
    try {
      const frame = await this.api.lastFrame(video.id);
      this.inputAsset.set(frame);
      this.type.set('ImageToVideo');
      this.prompt.set('');
      this.latestId.set(null);
      this.toast.success('Last frame loaded as the input image. Describe what happens next.');
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.extracting.set(false);
    }
  }

  protected track(promise: Promise<Generation | null>): void {
    void promise.then((g) => g && this.latestId.set(g.id));
  }
}
