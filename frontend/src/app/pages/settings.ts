import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { StatusService, ThemeService, ToastService } from '../core/app-state';
import { CatalogService } from '../core/catalog.service';
import { errorMessage } from '../core/errors';
import { SettingsResponse, StudioSettings } from '../core/models';
import { BytesPipe } from '../shared/format';
import { Icon } from '../shared/icon';

@Component({
  selector: 'app-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Icon, BytesPipe],
  template: `
    <div class="mx-auto max-w-5xl space-y-6">
      <div class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="text-2xl font-bold tracking-tight">Settings</h1>
          <p class="mt-1 text-sm text-muted">Stored in the local database. Defaults come from appsettings.json.</p>
        </div>
        <button type="button" class="btn btn-primary" [disabled]="!model() || saving()" (click)="save()">
          <app-icon name="check" [size]="16" /> {{ saving() ? 'Saving…' : 'Save settings' }}
        </button>
      </div>

      @if (model(); as s) {
        <!-- Cost control -->
        <section class="card overflow-hidden">
          <div class="flex flex-wrap items-center gap-4 border-b border-line p-5" [class]="s.localOnly ? 'bg-emerald-500/5' : 'bg-amber-500/5'">
            <app-icon name="zap" [size]="22" [class]="s.localOnly ? 'text-emerald-500' : 'text-amber-500'" />
            <div class="flex-1">
              <h2 class="text-lg font-semibold">LOCAL ONLY</h2>
              <p class="text-sm text-muted">Blocks every paid or remote AI API. Generation runs on your own machine.</p>
            </div>
            <label class="flex cursor-pointer items-center gap-3">
              <span class="text-sm font-medium">{{ s.localOnly ? 'On' : 'Off' }}</span>
              <input type="checkbox" class="peer sr-only" [(ngModel)]="s.localOnly" (ngModelChange)="touch()" />
              <span class="relative h-6 w-11 rounded-full bg-line transition peer-checked:bg-emerald-500 after:absolute after:left-0.5 after:top-0.5 after:h-5 after:w-5 after:rounded-full after:bg-white after:transition peer-checked:after:translate-x-5"></span>
            </label>
          </div>
          <div class="grid gap-6 p-5 md:grid-cols-2">
            <ul class="space-y-1.5 text-sm">
              @for (item of localChecklist(); track item.label) {
                <li class="flex items-center gap-2">
                  <app-icon [name]="item.ok ? 'check' : 'alert'" [size]="15" [class]="item.ok ? 'text-emerald-500' : 'text-amber-500'" />
                  {{ item.label }}
                  @if (item.note) { <span class="text-xs text-muted">— {{ item.note }}</span> }
                </li>
              }
            </ul>
            <p class="rounded-lg border border-line bg-panel-2 p-4 text-sm text-muted">
              <strong class="text-ink">Your actual cost depends on your hardware/electricity.</strong>
              No credits and no card are needed, but GPU inference still uses power, and if you point ComfyUI at a rented cloud GPU,
              that provider bills you for it. Cloud GPU inference is never free.
            </p>
          </div>
        </section>

        <!-- AI provider -->
        <section class="card space-y-4 p-5">
          <h2 class="font-semibold">AI provider</h2>
          <div class="grid gap-4 md:grid-cols-2">
            <div>
              <label class="label" for="prov">Provider</label>
              <select id="prov" class="input" [(ngModel)]="s.aiProvider" (ngModelChange)="touch()">
                <option value="ComfyUI">ComfyUI (local or LAN)</option>
                <option value="Kaggle">Kaggle free GPU (remote, batch mode)</option>
              </select>
              @if (status.status(); as st) {
                <p class="mt-1 text-xs" [class]="st.aiProvider.isAvailable ? 'text-emerald-500' : 'text-red-400'">{{ st.aiProvider.message }}</p>
              }
            </div>
            <div [class.opacity-60]="s.aiProvider === 'Kaggle'">
              <label class="label" for="curl">ComfyUI URL {{ s.aiProvider === 'Kaggle' ? '(fallback)' : '' }}</label>
              <div class="flex gap-2">
                <input id="curl" class="input" [(ngModel)]="s.comfyUiUrl" (ngModelChange)="touch()" placeholder="http://localhost:8188" />
                <button type="button" class="btn btn-secondary" (click)="testConnection()" [disabled]="testing()">{{ testing() ? 'Testing…' : 'Test' }}</button>
              </div>
            </div>
            <div class="md:col-span-2">
              <label class="label" for="mpath">ComfyUI models folder (for downloads)</label>
              <input id="mpath" class="input" [(ngModel)]="s.comfyUiModelsPath" (ngModelChange)="touch()" placeholder="e.g. C:\\ComfyUI_windows_portable\\ComfyUI\\models" />
              <p class="mt-1 text-xs text-muted">Only needed to download models from the Models page. Leave empty if you install models yourself.</p>
            </div>
          </div>

          @if (s.aiProvider === 'Kaggle') {
            <div class="space-y-4 rounded-lg border border-sky-500/30 bg-sky-500/5 p-4">
              <div class="text-sm">
                <p class="font-medium">Kaggle batch mode — how it works</p>
                <ul class="mt-1 list-disc space-y-0.5 pl-5 text-muted">
                  <li>Jobs are collected and sent as one private notebook run to Kaggle's free GPUs (T4 ×2, 15 GB each — not 30 GB combined).</li>
                  <li>Each run spends several minutes installing ComfyUI and downloading model weights before generating. There is no step-by-step progress.</li>
                  <li>A run cannot be stopped from Chitra once it has started; cancelling only discards its result.</li>
                  <li>Your prompts and input images are uploaded to Kaggle. Free within your weekly Kaggle GPU quota.</li>
                </ul>
              </div>
              @if (s.localOnly) {
                <p class="rounded-md bg-amber-500/10 p-2 text-sm text-amber-500">Local-Only mode is on, which blocks Kaggle. Turn it off above to use Kaggle.</p>
              }
              <div class="grid gap-4 md:grid-cols-3">
                <div>
                  <label class="label" for="kuser">Kaggle username</label>
                  <input id="kuser" class="input" [(ngModel)]="s.kaggleUsername" (ngModelChange)="touch()" placeholder="Detected from your API token" />
                </div>
                <div>
                  <label class="label" for="kslug">Notebook slug</label>
                  <input id="kslug" class="input" [(ngModel)]="s.kaggleKernelSlug" (ngModelChange)="touch()" />
                  <p class="mt-1 text-[11px] text-muted">A private notebook Chitra creates and re-runs. Don't edit it on Kaggle.</p>
                </div>
                <div>
                  <label class="label" for="kacc">Accelerator</label>
                  <select id="kacc" class="input" [(ngModel)]="s.kaggleAccelerator" (ngModelChange)="touch()">
                    <option value="NvidiaTeslaT4">GPU T4 ×2 (free)</option>
                    <option value="NvidiaTeslaP100">GPU P100 (free, if offered)</option>
                  </select>
                </div>
                <div class="md:col-span-3">
                  <label class="label" for="kpy">Python with the Kaggle client</label>
                  <input id="kpy" class="input" [(ngModel)]="s.kagglePythonPath" (ngModelChange)="touch()" placeholder="e.g. C:\\AI\\.tools\\kaggle\\Scripts\\python.exe" />
                  <p class="mt-1 text-[11px] text-muted">A Python that has <span class="font-mono">pip install kaggle</span>. Your API token stays in <span class="font-mono">~/.kaggle/access_token</span> — Chitra never stores it.</p>
                </div>
                <div>
                  <label class="label" for="kmax">Max jobs per run</label>
                  <input id="kmax" type="number" min="1" max="10" class="input" [(ngModel)]="s.kaggleMaxJobsPerRun" (ngModelChange)="touch()" />
                </div>
                <div>
                  <label class="label" for="kwin">Batch window (seconds)</label>
                  <input id="kwin" type="number" min="0" max="600" class="input" [(ngModel)]="s.kaggleBatchWindowSeconds" (ngModelChange)="touch()" />
                  <p class="mt-1 text-[11px] text-muted">Waits this long so several jobs share one run's setup.</p>
                </div>
                <div>
                  <label class="label" for="ktime">Run time limit (minutes)</label>
                  <input id="ktime" type="number" min="20" max="720" class="input" [(ngModel)]="s.kaggleRunTimeoutMinutes" (ngModelChange)="touch()" />
                </div>
              </div>
            </div>
          }
        </section>

        <!-- Defaults -->
        <section class="card space-y-4 p-5">
          <h2 class="font-semibold">Defaults</h2>
          <div class="grid gap-4 md:grid-cols-3">
            <div>
              <label class="label" for="dim">Default image model</label>
              <select id="dim" class="input" [(ngModel)]="s.defaultImageModel" (ngModelChange)="touch()">
                @for (m of imageModels(); track m.id) { <option [value]="m.id">{{ m.name }}</option> }
              </select>
            </div>
            <div>
              <label class="label" for="dvm">Default video model</label>
              <select id="dvm" class="input" [(ngModel)]="s.defaultVideoModel" (ngModelChange)="touch()">
                @for (m of videoModels(); track m.id) { <option [value]="m.id">{{ m.name }}</option> }
              </select>
            </div>
            <div>
              <label class="label" for="di2v">Default image-to-video model</label>
              <select id="di2v" class="input" [(ngModel)]="s.defaultImageToVideoModel" (ngModelChange)="touch()">
                @for (m of i2vModels(); track m.id) { <option [value]="m.id">{{ m.name }}</option> }
              </select>
            </div>
            <div>
              <label class="label" for="dir">Default image resolution</label>
              <select id="dir" class="input" [(ngModel)]="s.defaultImageResolution" (ngModelChange)="touch()">
                @for (r of ['512x512', '768x768', '1024x1024', '1216x832', '832x1216']; track r) { <option [value]="r">{{ r }}</option> }
              </select>
            </div>
            <div>
              <label class="label" for="dvr">Default video resolution</label>
              <select id="dvr" class="input" [(ngModel)]="s.defaultVideoResolution" (ngModelChange)="touch()">
                @for (r of ['512x512', '768x432', '1024x576', '432x768', '576x1024']; track r) { <option [value]="r">{{ r }}</option> }
              </select>
            </div>
            <div>
              <label class="label" for="dfps">Default FPS</label>
              <select id="dfps" class="input" [(ngModel)]="s.defaultFps" (ngModelChange)="touch()">
                @for (f of [12, 16, 24]; track f) { <option [ngValue]="f">{{ f }}</option> }
              </select>
            </div>
          </div>
        </section>

        <!-- Tools -->
        <section class="card space-y-4 p-5">
          <h2 class="font-semibold">Local tools</h2>
          <div class="grid gap-4 md:grid-cols-2">
            <div>
              <label class="label" for="ff">FFmpeg path</label>
              <input id="ff" class="input" [(ngModel)]="s.ffmpegPath" (ngModelChange)="touch()" placeholder="ffmpeg" />
              @if (status.status(); as st) {
                <p class="mt-1 text-xs" [class]="st.ffmpeg.isAvailable ? 'text-emerald-500' : 'text-amber-500'">{{ st.ffmpeg.message }}</p>
              }
            </div>
            <div>
              <label class="label" for="tts">TTS provider</label>
              <select id="tts" class="input" [(ngModel)]="s.ttsProvider" (ngModelChange)="touch()">
                <option value="Piper">Piper (local, recommended)</option>
                <option value="Coqui">Coqui TTS server (local)</option>
              </select>
              @if (status.status(); as st) {
                <p class="mt-1 text-xs" [class]="st.tts.isAvailable ? 'text-emerald-500' : 'text-amber-500'">{{ st.tts.message }}</p>
              }
            </div>
            @if (s.ttsProvider === 'Piper') {
              <div>
                <label class="label" for="pp">Piper executable</label>
                <input id="pp" class="input" [(ngModel)]="s.piperPath" (ngModelChange)="touch()" placeholder="piper" />
              </div>
              <div>
                <label class="label" for="pv">Piper voices folder</label>
                <input id="pv" class="input" [(ngModel)]="s.piperVoicesPath" (ngModelChange)="touch()" placeholder="Folder with *.onnx + *.onnx.json" />
              </div>
              <div>
                <label class="label" for="pdv">Default voice</label>
                <input id="pdv" class="input" [(ngModel)]="s.piperDefaultVoice" (ngModelChange)="touch()" placeholder="e.g. en_US-lessac-medium" />
              </div>
            } @else {
              <div>
                <label class="label" for="cq">Coqui server URL</label>
                <input id="cq" class="input" [(ngModel)]="s.coquiUrl" (ngModelChange)="touch()" />
              </div>
            }
          </div>
        </section>

        <!-- Performance & appearance -->
        <section class="card space-y-4 p-5">
          <h2 class="font-semibold">Queue & appearance</h2>
          <div class="grid gap-4 md:grid-cols-4">
            <div>
              <label class="label" for="mc">Max concurrent generations</label>
              <input id="mc" type="number" min="1" max="8" class="input" [(ngModel)]="s.maxConcurrentGenerations" (ngModelChange)="touch()" />
              <p class="mt-1 text-[11px] text-muted">ComfyUI runs jobs one at a time; 1 is best for a single GPU.</p>
            </div>
            <div>
              <label class="label" for="it">Image timeout (min)</label>
              <input id="it" type="number" min="1" class="input" [(ngModel)]="s.imageTimeoutMinutes" (ngModelChange)="touch()" />
            </div>
            <div>
              <label class="label" for="vt">Video timeout (min)</label>
              <input id="vt" type="number" min="1" class="input" [(ngModel)]="s.videoTimeoutMinutes" (ngModelChange)="touch()" />
            </div>
            <div>
              <label class="label" for="th">Theme</label>
              <select id="th" class="input" [(ngModel)]="s.theme" (ngModelChange)="touch()">
                <option value="dark">Dark</option>
                <option value="light">Light</option>
              </select>
            </div>
          </div>
        </section>

        <!-- Storage -->
        @if (info(); as i) {
          <section class="card space-y-2 p-5 text-sm">
            <h2 class="font-semibold">Storage & host</h2>
            <p><span class="text-muted">Storage path:</span> <span class="font-mono">{{ i.storagePath }}</span></p>
            <p><span class="text-muted">Database:</span> {{ i.databaseProvider }} · <span class="text-muted">Authentication:</span> {{ i.authMode }}</p>
            <p class="text-xs text-muted">Storage path, database and authentication are configured in appsettings.json or environment variables (restart required).</p>
          </section>
        }
      } @else {
        <p class="text-sm text-muted">{{ loadError() ?? 'Loading settings…' }}</p>
      }

      <!-- GPU / system -->
      @if (status.status(); as st) {
        <section class="card space-y-4 p-5">
          <div class="flex items-center justify-between">
            <h2 class="font-semibold">System information</h2>
            <span class="badge" [class]="ratingClass(st.hardwareRating)">{{ st.hardwareRating }}</span>
          </div>
          <p class="text-sm text-muted">{{ st.hardwareRatingReason }}</p>
          <dl class="grid gap-3 text-sm sm:grid-cols-2 lg:grid-cols-3">
            <div class="rounded-lg bg-panel-2 p-3">
              <dt class="label">GPU</dt>
              @for (g of st.system.gpus; track g.name) {
                <dd>{{ g.name }} <span class="text-muted">· {{ g.vramTotalMb * 1024 * 1024 | bytes }} VRAM{{ g.driverVersion ? ' · driver ' + g.driverVersion : '' }}</span></dd>
              } @empty {
                <dd class="text-muted">No NVIDIA GPU detected</dd>
              }
            </div>
            <div class="rounded-lg bg-panel-2 p-3">
              <dt class="label">CPU</dt>
              <dd>{{ st.system.cpuName }} <span class="text-muted">· {{ st.system.cpuCores }} threads</span></dd>
            </div>
            <div class="rounded-lg bg-panel-2 p-3">
              <dt class="label">RAM</dt>
              <dd>{{ st.system.totalRamBytes | bytes }} <span class="text-muted">· {{ st.system.availableRamBytes | bytes }} free</span></dd>
            </div>
            <div class="rounded-lg bg-panel-2 p-3">
              <dt class="label">Disk (storage)</dt>
              @if (st.system.disk; as d) {
                <dd>{{ d.freeBytes | bytes }} free <span class="text-muted">of {{ d.totalBytes | bytes }}</span></dd>
              } @else {
                <dd class="text-muted">Unknown</dd>
              }
            </div>
            <div class="rounded-lg bg-panel-2 p-3">
              <dt class="label">OS</dt>
              <dd>{{ st.system.os }}</dd>
            </div>
            <div class="rounded-lg bg-panel-2 p-3">
              <dt class="label">Queue</dt>
              <dd>{{ st.processingGenerations }} running · {{ st.queuedGenerations }} queued</dd>
            </div>
          </dl>
          @for (n of st.system.notes; track n) {
            <p class="text-xs text-muted">{{ n }}</p>
          }
        </section>
      }
    </div>
  `,
})
export class SettingsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly theme = inject(ThemeService);
  private readonly catalog = inject(CatalogService);
  protected readonly status = inject(StatusService);

  protected readonly model = signal<StudioSettings | null>(null);
  protected readonly info = signal<SettingsResponse | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly testing = signal(false);
  /** Bumped on every edit so computed views re-read the mutable model. */
  private readonly edits = signal(0);

  protected readonly imageModels = computed(() => this.catalog.models().filter((m) => m.capabilities.includes('TextToImage')));
  protected readonly videoModels = computed(() => this.catalog.models().filter((m) => m.capabilities.includes('TextToVideo')));
  protected readonly i2vModels = computed(() => this.catalog.models().filter((m) => m.capabilities.includes('ImageToVideo')));

  protected readonly localChecklist = computed(() => {
    this.edits();
    const s = this.model();
    const st = this.status.status();
    const localOnly = s?.localOnly ?? true;
    return [
      { label: 'No paid API calls', ok: localOnly, note: localOnly ? '' : 'Local-Only is off' },
      { label: 'Local ComfyUI', ok: !!st?.aiProvider.isAvailable, note: st?.aiProvider.isAvailable ? '' : 'not reachable' },
      { label: 'Local storage', ok: !!st?.storage.isAvailable, note: '' },
      { label: 'Local FFmpeg', ok: !!st?.ffmpeg.isAvailable, note: st?.ffmpeg.isAvailable ? '' : 'not installed' },
      { label: 'Local TTS', ok: !!st?.tts.isAvailable, note: st?.tts.isAvailable ? '' : 'not configured' },
    ];
  });

  async ngOnInit(): Promise<void> {
    void this.catalog.ensureLoaded();
    try {
      const response = await this.api.settings();
      this.info.set(response);
      this.model.set({ ...response.settings });
    } catch (e) {
      this.loadError.set(errorMessage(e));
    }
  }

  protected touch(): void {
    this.edits.update((v) => v + 1);
  }

  protected async save(): Promise<void> {
    const s = this.model();
    if (!s) return;
    this.saving.set(true);
    try {
      const response = await this.api.saveSettings({
        ...s,
        defaultFps: Number(s.defaultFps),
        maxConcurrentGenerations: Number(s.maxConcurrentGenerations),
        kaggleUsername: s.kaggleUsername?.trim() || null,
        kaggleMaxJobsPerRun: Number(s.kaggleMaxJobsPerRun),
        kaggleBatchWindowSeconds: Number(s.kaggleBatchWindowSeconds),
        kaggleRunTimeoutMinutes: Number(s.kaggleRunTimeoutMinutes),
        imageTimeoutMinutes: Number(s.imageTimeoutMinutes),
        videoTimeoutMinutes: Number(s.videoTimeoutMinutes),
      });
      this.info.set(response);
      this.model.set({ ...response.settings });
      if (response.settings.theme === 'dark' || response.settings.theme === 'light') this.theme.theme.set(response.settings.theme);
      this.toast.success('Settings saved');
      await Promise.all([this.status.refresh(), this.catalog.reloadModels().catch(() => undefined)]);
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.saving.set(false);
    }
  }

  protected async testConnection(): Promise<void> {
    this.testing.set(true);
    await this.save();
    this.testing.set(false);
  }

  protected ratingClass(rating: string): string {
    return rating === 'Recommended' ? 'bg-emerald-500/15 text-emerald-500' : rating === 'Limited' ? 'bg-amber-500/15 text-amber-500' : 'bg-red-500/15 text-red-400';
  }
}
