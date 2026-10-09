import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../core/api.service';
import { StatusService, ToastService } from '../core/app-state';
import { CatalogService } from '../core/catalog.service';
import { errorMessage } from '../core/errors';
import { AiModel, GenerationType, ModelDownloadPlan, ModelDownloadStatus } from '../core/models';
import { RealtimeService } from '../core/realtime.service';
import { BytesPipe, typeLabel } from '../shared/format';
import { Icon } from '../shared/icon';

@Component({
  selector: 'app-models',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Icon, BytesPipe],
  template: `
    <div class="mx-auto max-w-7xl space-y-6">
      <div class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="text-2xl font-bold tracking-tight">Models</h1>
          <p class="mt-1 text-sm text-muted">Open models run through ComfyUI. Install state is read live from ComfyUI; readiness compares each model with your GPU.</p>
        </div>
        <button type="button" class="btn btn-secondary" (click)="refresh()" [disabled]="loading()"><app-icon name="refresh" [size]="16" /> Refresh</button>
      </div>

      @if (status.status(); as s) {
        @if (!s.aiProvider.isAvailable) {
          <p class="rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-sm text-amber-500">
            ComfyUI is offline, so install state is unknown. {{ s.aiProvider.message }}
          </p>
        }
      }

      <div class="flex flex-wrap gap-1.5">
        @for (f of filters; track f.key) {
          <button type="button" class="chip" [class.chip-active]="filter() === f.key" (click)="filter.set(f.key)">{{ f.label }}</button>
        }
      </div>

      <div class="grid gap-4 lg:grid-cols-2">
        @for (m of visible(); track m.id) {
          @let dl = download(m);
          <article class="card flex flex-col p-5">
            <div class="flex flex-wrap items-start gap-2">
              <div class="min-w-0 flex-1">
                <h2 class="font-semibold">{{ m.name }}</h2>
                <p class="text-xs text-muted">{{ m.family }} · {{ m.license ?? 'see license' }}</p>
              </div>
              <span class="badge" [class]="installClass(m)">{{ installLabel(m) }}</span>
              <span class="badge" [class]="readinessClass(m)" [title]="m.readinessReason">{{ m.readiness }}</span>
            </div>
            @if (m.description) { <p class="mt-2 text-sm text-muted">{{ m.description }}</p> }
            <div class="mt-3 flex flex-wrap gap-1.5 text-[11px]">
              @for (c of m.capabilities; track c) { <span class="badge bg-panel-2">{{ label(c) }}</span> }
              <span class="badge bg-panel-2">{{ m.totalSizeBytes | bytes }} download</span>
              <span class="badge bg-panel-2">{{ m.minVramGb }} GB min · {{ m.recommendedVramGb }} GB recommended VRAM</span>
            </div>
            <p class="mt-2 text-xs text-muted">{{ m.readinessReason }}</p>
            @if (m.notes) { <p class="mt-1 text-xs text-muted">{{ m.notes }}</p> }

            <details class="mt-3 text-xs">
              <summary class="cursor-pointer text-muted">Files ({{ m.files.length }})</summary>
              <table class="mt-2 w-full">
                @for (f of m.files; track f.fileName) {
                  <tr class="border-t border-line">
                    <td class="py-1.5 pr-2 font-mono">models/{{ f.folder }}/{{ f.fileName }}</td>
                    <td class="py-1.5 pr-2 text-right text-muted">{{ f.sizeBytes | bytes }}</td>
                    <td class="py-1.5 text-right">
                      @if (f.installed === true) { <span class="text-emerald-500">installed</span> }
                      @else if (f.installed === false) { <span class="text-red-400">missing</span> }
                      @else { <span class="text-muted">unknown</span> }
                    </td>
                  </tr>
                }
              </table>
            </details>

            @if (dl && (dl.state === 'Downloading' || dl.state === 'Pending')) {
              <div class="mt-3">
                <div class="flex justify-between text-xs text-muted">
                  <span>{{ dl.currentFile ?? 'Starting…' }}</span>
                  <span>{{ dl.bytesDownloaded | bytes }} / {{ dl.totalBytes | bytes }} · {{ dl.progress }}%</span>
                </div>
                <div class="mt-1 h-1.5 overflow-hidden rounded-full bg-line"><div class="h-full bg-gradient-to-r from-amber-500 to-rose-500 transition-all" [style.width.%]="dl.progress"></div></div>
              </div>
            } @else if (dl?.error) {
              <p class="mt-3 text-xs text-red-400">{{ dl!.error }}</p>
            } @else if (dl?.state === 'Completed') {
              <p class="mt-3 text-xs text-emerald-500">Download complete. ComfyUI picks up new files automatically (press Refresh if it does not).</p>
            }

            <div class="mt-auto flex flex-wrap gap-2 pt-4">
              @if (dl && (dl.state === 'Downloading' || dl.state === 'Pending')) {
                <button type="button" class="btn btn-secondary btn-sm" (click)="cancel(m)"><app-icon name="stop" [size]="14" /> Cancel download</button>
              } @else if (m.installState !== 'Installed' && m.installState !== 'OnDemand') {
                <button type="button" class="btn btn-primary btn-sm" (click)="openPlan(m)"><app-icon name="download" [size]="14" /> Download…</button>
              }
              @if (m.homepage) {
                <a class="btn btn-ghost btn-sm" [href]="m.homepage" target="_blank" rel="noopener"><app-icon name="link" [size]="14" /> Model page</a>
              }
            </div>
          </article>
        }
      </div>

      <p class="text-xs text-muted">
        CogVideoX and other models that need ComfyUI custom nodes can be added by exporting their workflow in ComfyUI's API format into
        <span class="font-mono">backend/src/Chitra.Api/ai/workflows</span> and adding an entry to <span class="font-mono">ai/models.json</span> — see ARCHITECTURE.md.
      </p>
    </div>

    @if (plan(); as p) {
      <div class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" (click)="plan.set(null)">
        <div class="card w-full max-w-2xl p-5" (click)="$event.stopPropagation()" role="dialog" aria-modal="true">
          <h2 class="text-lg font-semibold">Download {{ p.modelName }}?</h2>
          <p class="mt-1 text-sm text-muted">Review the sizes below. Nothing is downloaded until you confirm.</p>
          <table class="mt-4 w-full text-sm">
            @for (f of p.files; track f.fileName) {
              <tr class="border-t border-line">
                <td class="py-2 pr-2">
                  <span class="font-mono text-xs">{{ f.fileName }}</span>
                  @if (f.targetPath) { <span class="block text-[11px] text-muted">→ {{ f.targetPath }}</span> }
                </td>
                <td class="py-2 text-right">
                  @if (f.alreadyPresent) { <span class="text-emerald-500">already present</span> }
                  @else { {{ f.sizeBytes | bytes }}{{ f.sizeIsExact ? '' : ' (approx.)' }} }
                </td>
              </tr>
            }
          </table>
          <div class="mt-4 rounded-lg bg-panel-2 p-3 text-sm">
            <p>Total download: <strong>{{ p.totalBytesToDownload | bytes }}</strong></p>
            @if (p.targetDirectory) { <p class="text-muted">Free space on target disk: {{ p.freeDiskBytes | bytes }}</p> }
          </div>
          @if (p.reason) {
            <p class="mt-3 text-sm text-amber-500">{{ p.reason }}</p>
            @if (!p.targetDirectory) { <a routerLink="/settings" class="text-sm text-amber-500 underline" (click)="plan.set(null)">Open Settings</a> }
          }
          <div class="mt-5 flex justify-end gap-2">
            <button type="button" class="btn btn-ghost" (click)="plan.set(null)">Cancel</button>
            <button type="button" class="btn btn-primary" [disabled]="!p.canDownload || starting()" (click)="confirm(p)">
              <app-icon name="download" [size]="16" /> Download {{ p.totalBytesToDownload | bytes }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
})
export class ModelsPage implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly realtime = inject(RealtimeService);
  private readonly catalog = inject(CatalogService);
  protected readonly status = inject(StatusService);

  protected readonly filters: { key: GenerationType | ''; label: string }[] = [
    { key: '', label: 'All' },
    { key: 'TextToImage', label: 'Image' },
    { key: 'TextToVideo', label: 'Text to video' },
    { key: 'ImageToVideo', label: 'Image to video' },
  ];
  protected readonly filter = signal<GenerationType | ''>('');
  protected readonly models = signal<AiModel[]>([]);
  protected readonly loading = signal(false);
  protected readonly plan = signal<ModelDownloadPlan | null>(null);
  protected readonly starting = signal(false);
  private readonly downloads = signal<Record<string, ModelDownloadStatus>>({});
  protected readonly visible = computed(() => this.models().filter((m) => !this.filter() || m.capabilities.includes(this.filter() as GenerationType)));
  private subscription?: Subscription;

  ngOnInit(): void {
    this.subscription = this.realtime.modelDownloadUpdated.subscribe((s) => {
      this.downloads.update((d) => ({ ...d, [s.modelId]: s }));
      if (s.state === 'Completed') {
        this.toast.success('Model download complete');
        void this.refresh();
      }
    });
    void this.refresh();
  }

  ngOnDestroy(): void {
    this.subscription?.unsubscribe();
  }

  protected async refresh(): Promise<void> {
    this.loading.set(true);
    try {
      const models = await this.api.models();
      this.models.set(models);
      this.catalog.models.set(models);
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  protected download(m: AiModel): ModelDownloadStatus | null {
    return this.downloads()[m.id] ?? m.download;
  }

  protected async openPlan(m: AiModel): Promise<void> {
    try {
      this.plan.set(await this.api.downloadPlan(m.id));
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  protected async confirm(p: ModelDownloadPlan): Promise<void> {
    this.starting.set(true);
    try {
      const s = await this.api.startDownload(p.modelId);
      this.downloads.update((d) => ({ ...d, [s.modelId]: s }));
      this.plan.set(null);
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.starting.set(false);
    }
  }

  protected async cancel(m: AiModel): Promise<void> {
    try {
      await this.api.cancelDownload(m.id);
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  protected label(c: GenerationType): string {
    return typeLabel[c] === 'Image' ? 'Text to image' : typeLabel[c];
  }

  protected installLabel(m: AiModel): string {
    return { Installed: 'Installed', Partial: 'Partially installed', NotInstalled: 'Not installed', Unknown: 'Install state unknown', OnDemand: 'Downloaded per Kaggle run' }[m.installState];
  }

  protected installClass(m: AiModel): string {
    return m.installState === 'Installed' ? 'bg-emerald-500/15 text-emerald-500' : m.installState === 'OnDemand' ? 'bg-sky-500/15 text-sky-400' : m.installState === 'Unknown' ? 'bg-panel-2 text-muted' : 'bg-red-500/15 text-red-400';
  }

  protected readinessClass(m: AiModel): string {
    return m.readiness === 'Recommended' ? 'bg-emerald-500/15 text-emerald-500' : m.readiness === 'Limited' ? 'bg-amber-500/15 text-amber-500' : 'bg-red-500/15 text-red-400';
  }
}
