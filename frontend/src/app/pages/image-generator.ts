import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { StatusService } from '../core/app-state';
import { CatalogService } from '../core/catalog.service';
import { errorCode, errorMessage } from '../core/errors';
import { GenerationStore } from '../core/generation-store';
import { Generation } from '../core/models';
import { GenerationActions } from '../shared/generation-actions';
import { GenerationCard } from '../shared/generation-card';
import { Icon } from '../shared/icon';
import { ModelSelect } from '../shared/model-select';
import { PromptField } from '../shared/prompt-field';
import { StyleCharacterFields } from '../shared/style-character-fields';

const ASPECTS: { label: string; w: number; h: number }[] = [
  { label: '1:1', w: 1, h: 1 },
  { label: '3:2', w: 3, h: 2 },
  { label: '2:3', w: 2, h: 3 },
  { label: '16:9', w: 16, h: 9 },
  { label: '9:16', w: 9, h: 16 },
];

const SAMPLERS = ['euler', 'euler_ancestral', 'dpmpp_2m', 'dpmpp_2m_sde', 'dpmpp_sde', 'uni_pc', 'ddim'];
const SCHEDULERS = ['normal', 'karras', 'exponential', 'simple', 'sgm_uniform'];

@Component({
  selector: 'app-image-generator',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, Icon, PromptField, ModelSelect, StyleCharacterFields, GenerationCard],
  template: `
    <div class="mx-auto grid max-w-7xl gap-6 lg:grid-cols-[minmax(0,26rem)_minmax(0,1fr)]">
      <!-- Controls -->
      <form class="card h-fit space-y-5 p-5" (ngSubmit)="generate()">
        <div>
          <h1 class="text-xl font-semibold">Image Generator</h1>
          <p class="text-sm text-muted">Text to image on your local ComfyUI.</p>
        </div>

        <app-prompt-field [(value)]="prompt" type="TextToImage" [styleHint]="styleName()" [onSubmit]="submitFn" />

        <div>
          <label class="label" for="neg">Negative prompt</label>
          <textarea id="neg" name="neg" class="input min-h-16" [(ngModel)]="negative" placeholder="Things to avoid (a sensible default is used when empty)"></textarea>
        </div>

        <app-model-select [models]="models()" [(value)]="modelId" />

        <div class="grid gap-4">
          <app-style-character-fields [(styleId)]="styleId" [(characterIds)]="characterIds" />
        </div>

        <div>
          <span class="label">Aspect ratio</span>
          <div class="seg">
            @for (a of aspects; track a.label) {
              <button type="button" [class.on]="aspect() === a.label" (click)="setAspect(a.label)">{{ a.label }}</button>
            }
          </div>
          <p class="mt-1 text-[11px] text-muted">{{ width() }} × {{ height() }} px</p>
        </div>

        <div>
          <span class="label">Images</span>
          <div class="seg">
            @for (n of [1, 2, 3, 4]; track n) {
              <button type="button" [class.on]="batch() === n" (click)="batch.set(n)">{{ n }}</button>
            }
          </div>
        </div>

        <label class="flex items-center gap-2 text-sm">
          <input type="checkbox" name="enh" [(ngModel)]="autoEnhance" class="h-4 w-4 accent-amber-500" />
          Auto-enhance prompt when generating
        </label>

        <details class="group rounded-lg border border-line p-3" [open]="advanced()" (toggle)="advanced.set($any($event.target).open)">
          <summary class="cursor-pointer text-sm font-medium">Advanced settings</summary>
          <div class="mt-4 grid grid-cols-2 gap-3">
            <div>
              <label class="label" for="w">Width</label>
              <input id="w" name="w" type="number" class="input" step="64" min="256" max="2048" [(ngModel)]="width" />
            </div>
            <div>
              <label class="label" for="h">Height</label>
              <input id="h" name="h" type="number" class="input" step="64" min="256" max="2048" [(ngModel)]="height" />
            </div>
            <div>
              <label class="label" for="steps">Steps · {{ steps() }}</label>
              <input id="steps" name="steps" type="range" min="1" max="80" class="w-full" [(ngModel)]="steps" />
            </div>
            <div>
              <label class="label" for="cfg">CFG scale · {{ cfg() }}</label>
              <input id="cfg" name="cfg" type="range" min="1" max="20" step="0.5" class="w-full" [(ngModel)]="cfg" />
            </div>
            <div>
              <label class="label" for="sampler">Sampler</label>
              <select id="sampler" name="sampler" class="input" [(ngModel)]="sampler">
                @for (s of samplers; track s) { <option [value]="s">{{ s }}</option> }
              </select>
            </div>
            <div>
              <label class="label" for="scheduler">Scheduler</label>
              <select id="scheduler" name="scheduler" class="input" [(ngModel)]="scheduler">
                @for (s of schedulers; track s) { <option [value]="s">{{ s }}</option> }
              </select>
            </div>
            <div class="col-span-2">
              <label class="label" for="seed">Seed</label>
              <div class="flex gap-2">
                <input id="seed" name="seed" type="number" class="input" [disabled]="randomSeed()" [(ngModel)]="seed" placeholder="Random" />
                <label class="flex items-center gap-1.5 whitespace-nowrap text-sm">
                  <input type="checkbox" name="rs" [(ngModel)]="randomSeed" class="h-4 w-4 accent-amber-500" /> Random
                </label>
              </div>
            </div>
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

        <button type="submit" class="btn btn-primary w-full py-2.5" [disabled]="!prompt().trim() || submitting()">
          <app-icon name="sparkles" [size]="16" /> {{ submitting() ? 'Queuing…' : 'Generate' }}
        </button>
      </form>

      <!-- Results -->
      <section class="min-w-0 space-y-4">
        <div class="card flex min-h-[24rem] items-center justify-center overflow-hidden">
          @if (latest(); as g) {
            @if (g.outputs.length) {
              <div class="grid w-full gap-1" [class.grid-cols-2]="g.outputs.length > 1">
                @for (o of g.outputs; track o.id) {
                  <button type="button" class="bg-black" (click)="actions.view(g)">
                    <img [src]="o.url" class="mx-auto max-h-[70vh] w-full object-contain" alt="Generated image" />
                  </button>
                }
              </div>
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
                <p class="mt-2 text-xs text-muted">{{ g.progress }}% · runs in the background, you can leave this page</p>
                <button type="button" class="btn btn-ghost btn-sm mt-3" (click)="actions.cancel(g)"><app-icon name="stop" [size]="14" /> Cancel</button>
              </div>
            }
          } @else {
            <div class="p-10 text-center text-muted">
              <app-icon name="image" [size]="36" />
              <p class="mt-3 text-sm">Your images will appear here.</p>
            </div>
          }
        </div>

        @if (latest(); as g) {
          @if (g.status === 'Completed') {
            <div class="flex flex-wrap gap-2">
              <a class="btn btn-secondary btn-sm" [href]="g.outputs[0].downloadUrl" download><app-icon name="download" [size]="14" /> Download</a>
              <button type="button" class="btn btn-secondary btn-sm" (click)="track(actions.regenerate(g))"><app-icon name="refresh" [size]="14" /> Regenerate</button>
              <button type="button" class="btn btn-secondary btn-sm" (click)="track(actions.variation(g))"><app-icon name="shuffle" [size]="14" /> Variation</button>
              <button type="button" class="btn btn-secondary btn-sm" (click)="actions.animate(g.outputs[0].id)"><app-icon name="video" [size]="14" /> Animate</button>
              <button type="button" class="btn btn-secondary btn-sm" (click)="actions.useInProject(g)"><app-icon name="folder" [size]="14" /> Use in project</button>
              <span class="ml-auto self-center font-mono text-xs text-muted">seed {{ g.seed }}</span>
            </div>
          }
          @if (g.warnings.length) {
            <div class="rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-xs text-amber-500">
              @for (w of g.warnings; track w) { <p>{{ w }}</p> }
            </div>
          }
        }

        <div>
          <h2 class="mb-3 text-sm font-semibold text-muted">Recent images</h2>
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
export class ImageGeneratorPage implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly store = inject(GenerationStore);
  private readonly status = inject(StatusService);
  protected readonly catalog = inject(CatalogService);
  protected readonly actions = inject(GenerationActions);

  protected readonly aspects = ASPECTS;
  protected readonly samplers = SAMPLERS;
  protected readonly schedulers = SCHEDULERS;

  protected readonly prompt = signal('');
  protected readonly negative = signal('');
  protected readonly modelId = signal('');
  protected readonly styleId = signal<string | null>(null);
  protected readonly characterIds = signal<string[]>([]);
  protected readonly aspect = signal('1:1');
  protected readonly width = signal(1024);
  protected readonly height = signal(1024);
  protected readonly batch = signal(1);
  protected readonly steps = signal(30);
  protected readonly cfg = signal(7);
  protected readonly sampler = signal('euler');
  protected readonly scheduler = signal('normal');
  protected readonly seed = signal<number | null>(null);
  protected readonly randomSeed = signal(true);
  protected readonly autoEnhance = signal(false);
  protected readonly advanced = signal(false);
  protected readonly submitting = signal(false);
  protected readonly error = signal<{ message: string; code: string | null } | null>(null);

  protected readonly latestId = signal<string | null>(null);
  protected readonly latest = this.store.watch(this.latestId);
  protected readonly history = signal<Generation[]>([]);

  protected readonly models = computed(() => this.catalog.models().filter((m) => m.capabilities.includes('TextToImage')));
  protected readonly styleName = computed(() => this.catalog.styles().find((s) => s.id === this.styleId())?.name ?? null);
  protected readonly submitFn = () => void this.generate();
  private unsubscribe?: () => void;

  constructor() {
    // Apply model defaults whenever the selected model changes.
    effect(() => {
      const model = this.models().find((m) => m.id === this.modelId());
      if (!model) return;
      untracked(() => {
        this.steps.set(model.defaults.steps);
        this.cfg.set(model.defaults.cfg);
        this.setAspect(this.aspect(), model.defaults.width);
      });
    });
  }

  async ngOnInit(): Promise<void> {
    this.unsubscribe = this.store.onUpdate((g) => {
      if (g.type !== 'TextToImage') return;
      this.history.update((list) => (list.some((x) => x.id === g.id) ? list.map((x) => (x.id === g.id ? g : x)) : [g, ...list]));
    });

    await this.catalog.ensureLoaded();
    const settings = await this.api.settings().catch(() => null);
    const preferred = settings?.settings.defaultImageModel;
    const models = this.models();
    this.modelId.set(models.find((m) => m.id === preferred)?.id ?? models.find((m) => m.installState === 'Installed')?.id ?? models[0]?.id ?? '');

    try {
      const page = await this.api.generations({ filter: 'images', pageSize: 12 });
      page.items.forEach((g) => this.store.upsert(g));
      this.history.set(page.items);
    } catch {
      // Shell shows connectivity problems.
    }
  }

  ngOnDestroy(): void {
    this.unsubscribe?.();
  }

  protected setAspect(label: string, base?: number): void {
    const a = ASPECTS.find((x) => x.label === label) ?? ASPECTS[0];
    const side = base ?? this.models().find((m) => m.id === this.modelId())?.defaults.width ?? 1024;
    // Keep the pixel count near side², rounded to multiples of 64.
    const scale = Math.sqrt((side * side) / (a.w * a.h));
    this.aspect.set(label);
    this.width.set(Math.round((a.w * scale) / 64) * 64);
    this.height.set(Math.round((a.h * scale) / 64) * 64);
  }

  protected async generate(): Promise<void> {
    if (!this.prompt().trim() || this.submitting()) return;
    this.submitting.set(true);
    this.error.set(null);
    try {
      const g = await this.api.createImage({
        prompt: this.prompt(),
        negativePrompt: this.negative() || null,
        modelId: this.modelId() || null,
        width: Number(this.width()),
        height: Number(this.height()),
        steps: Number(this.steps()),
        cfgScale: Number(this.cfg()),
        seed: this.randomSeed() || this.seed() === null ? null : Number(this.seed()),
        batchSize: this.batch(),
        sampler: this.sampler(),
        scheduler: this.scheduler(),
        styleId: this.styleId(),
        characterIds: this.characterIds(),
        enhancePrompt: this.autoEnhance(),
      });
      this.store.upsert(g);
      this.latestId.set(g.id);
      void this.status.refresh();
    } catch (e) {
      this.error.set({ message: errorMessage(e), code: errorCode(e) });
    } finally {
      this.submitting.set(false);
    }
  }

  protected track(promise: Promise<Generation | null>): void {
    void promise.then((g) => g && this.latestId.set(g.id));
  }
}
