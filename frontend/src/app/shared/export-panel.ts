import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ApiService } from '../core/api.service';
import { StatusService, ToastService } from '../core/app-state';
import { errorMessage } from '../core/errors';
import { MediaAsset, ProjectDetail, VideoExport, isTerminal } from '../core/models';
import { RealtimeService } from '../core/realtime.service';
import { BytesPipe } from './format';
import { Icon } from './icon';

const TRANSITIONS = [
  { value: 'none', label: 'Hard cut' },
  { value: 'fade', label: 'Cross-fade' },
  { value: 'dissolve', label: 'Dissolve' },
  { value: 'fadeblack', label: 'Fade through black' },
  { value: 'wipeleft', label: 'Wipe left' },
  { value: 'slideleft', label: 'Slide left' },
  { value: 'circleopen', label: 'Circle open' },
];

/** Final video export: concatenates scenes with FFmpeg, adding transitions, voice-over, subtitles and music. */
@Component({
  selector: 'app-export-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Icon, DatePipe, BytesPipe],
  template: `
    <section class="card p-5">
      <div class="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 class="text-lg font-semibold">Final video export</h2>
          <p class="text-sm text-muted">Rendered locally with FFmpeg. Scenes without video use their keyframe as a still shot.</p>
        </div>
        @if (status.status(); as st) {
          <span class="badge" [class]="st.ffmpeg.isAvailable ? 'bg-emerald-500/15 text-emerald-500' : 'bg-red-500/15 text-red-400'" [title]="st.ffmpeg.message">
            FFmpeg {{ st.ffmpeg.isAvailable ? 'ready' : 'not found' }}
          </span>
        }
      </div>

      @if (status.status()?.ffmpeg?.isAvailable === false) {
        <p class="mt-3 rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-sm text-amber-500">{{ status.status()?.ffmpeg?.message }}</p>
      }

      <div class="mt-4 grid gap-4 md:grid-cols-4">
        <div>
          <span class="label">Resolution</span>
          <div class="seg">
            @for (r of [480, 720, 1080]; track r) {
              <button type="button" [class.on]="resolution() === r" (click)="resolution.set(r)">{{ r }}p</button>
            }
          </div>
          <p class="mt-1 text-[11px] text-muted">1080p upscales clips if they were generated smaller.</p>
        </div>
        <div>
          <label class="label" for="xfps">FPS</label>
          <select id="xfps" class="input" [(ngModel)]="fps">
            @for (f of [16, 24, 30]; track f) { <option [ngValue]="f">{{ f }}</option> }
          </select>
        </div>
        <div>
          <label class="label" for="xtr">Transition</label>
          <select id="xtr" class="input" [(ngModel)]="transition">
            @for (t of transitions; track t.value) { <option [value]="t.value">{{ t.label }}</option> }
          </select>
        </div>
        <div>
          <label class="label" for="xts">Transition length · {{ transitionSeconds() }}s</label>
          <input id="xts" type="range" min="0.2" max="2" step="0.1" class="w-full" [disabled]="transition() === 'none'" [(ngModel)]="transitionSeconds" />
        </div>
      </div>

      <div class="mt-4 grid gap-4 md:grid-cols-3">
        <label class="flex items-center gap-2 text-sm">
          <input type="checkbox" class="h-4 w-4 accent-amber-500" [(ngModel)]="includeNarration" />
          Include voice-over ({{ narratedScenes() }}/{{ project().scenes.length }} scenes)
        </label>
        <label class="flex items-center gap-2 text-sm">
          <input type="checkbox" class="h-4 w-4 accent-amber-500" [(ngModel)]="includeSubtitles" />
          Burn in subtitles from narration
        </label>
        <div>
          <span class="label">Background music</span>
          @if (music(); as m) {
            <div class="flex items-center gap-2 text-sm">
              <app-icon name="music" [size]="15" class="text-amber-500" />
              <span class="flex-1 truncate">{{ m.originalFileName ?? m.fileName }}</span>
              <button type="button" class="btn-icon" (click)="music.set(null)" aria-label="Remove music"><app-icon name="x" [size]="14" /></button>
            </div>
            <label class="mt-2 block text-xs text-muted">Volume · {{ musicVolume() }}
              <input type="range" min="0" max="1" step="0.05" class="w-full" [(ngModel)]="musicVolume" />
            </label>
          } @else {
            <label class="btn btn-secondary btn-sm cursor-pointer">
              <app-icon name="upload" [size]="14" /> {{ uploading() ? 'Uploading…' : 'Upload audio' }}
              <input type="file" accept="audio/*" class="hidden" (change)="uploadMusic($event)" />
            </label>
          }
        </div>
      </div>

      @if (missingMedia() > 0) {
        <p class="mt-4 text-sm text-amber-500">{{ missingMedia() }} scene(s) have neither video nor keyframe and cannot be exported yet.</p>
      }

      <button type="button" class="btn btn-primary mt-5" [disabled]="exporting() || missingMedia() > 0 || !project().scenes.length" (click)="startExport()">
        <app-icon name="film" [size]="16" /> {{ exporting() ? 'Starting…' : 'Export MP4' }}
      </button>

      @if (exports().length) {
        <div class="mt-6 space-y-3 border-t border-line pt-4">
          @for (e of exports(); track e.id) {
            <div class="rounded-lg border border-line p-3">
              <div class="flex flex-wrap items-center gap-2 text-sm">
                <span class="font-medium">{{ e.resolution }}p · {{ e.transition === 'none' ? 'cuts' : e.transition }}</span>
                <span class="text-xs text-muted">{{ e.createdAt | date: 'short' }}</span>
                <span class="ml-auto text-xs" [class]="e.status === 'Failed' ? 'text-red-400' : e.status === 'Completed' ? 'text-emerald-500' : 'text-muted'">
                  {{ e.status }}{{ isTerminal(e.status) ? '' : ' · ' + e.progress + '%' }}
                </span>
                @if (!isTerminal(e.status)) {
                  <button type="button" class="btn btn-ghost btn-sm" (click)="cancel(e)">Cancel</button>
                }
              </div>
              @if (!isTerminal(e.status)) {
                <div class="mt-2 h-1.5 overflow-hidden rounded-full bg-line"><div class="h-full bg-gradient-to-r from-amber-500 to-rose-500 transition-all" [style.width.%]="e.progress"></div></div>
                <p class="mt-1 text-xs text-muted">{{ e.statusMessage }}</p>
              }
              @if (e.errorMessage) { <p class="mt-2 text-xs text-red-400">{{ e.errorMessage }}</p> }
              @if (e.output; as o) {
                <video [src]="o.url" controls playsinline class="mt-3 max-h-96 w-full rounded-md bg-black"></video>
                <a class="btn btn-secondary btn-sm mt-2" [href]="o.downloadUrl" download><app-icon name="download" [size]="14" /> Download MP4 ({{ o.sizeBytes | bytes }})</a>
              }
            </div>
          }
        </div>
      }
    </section>
  `,
})
export class ExportPanel implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly realtime = inject(RealtimeService);
  protected readonly status = inject(StatusService);

  readonly project = input.required<ProjectDetail>();
  readonly exported = output<void>();

  protected readonly transitions = TRANSITIONS;
  protected readonly isTerminal = isTerminal;
  protected readonly resolution = signal(720);
  protected readonly fps = signal(24);
  protected readonly transition = signal('fade');
  protected readonly transitionSeconds = signal(0.5);
  protected readonly includeNarration = signal(true);
  protected readonly includeSubtitles = signal(false);
  protected readonly music = signal<MediaAsset | null>(null);
  protected readonly musicVolume = signal(0.25);
  protected readonly uploading = signal(false);
  protected readonly exporting = signal(false);
  protected readonly exports = signal<VideoExport[]>([]);

  protected readonly narratedScenes = computed(() => this.project().scenes.filter((s) => s.narrationAudio).length);
  protected readonly missingMedia = computed(() => this.project().scenes.filter((s) => !s.video && !s.image).length);
  private subscription?: Subscription;
  private poll?: ReturnType<typeof setInterval>;

  constructor() {
    effect(() => {
      const id = this.project().id;
      untracked(() => void this.load(id));
    });
  }

  ngOnInit(): void {
    this.subscription = this.realtime.exportUpdated.subscribe((e) => this.apply(e));
    // Fallback when live updates are disconnected.
    this.poll = setInterval(() => {
      if (!this.realtime.connected() && this.exports().some((e) => !isTerminal(e.status))) void this.load(this.project().id);
    }, 3000);
  }

  ngOnDestroy(): void {
    this.subscription?.unsubscribe();
    clearInterval(this.poll);
  }

  protected async uploadMusic(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.uploading.set(true);
    try {
      this.music.set(await this.api.upload(file, this.project().id));
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.uploading.set(false);
    }
  }

  protected async startExport(): Promise<void> {
    this.exporting.set(true);
    try {
      const e = await this.api.createExport(this.project().id, {
        resolution: this.resolution(),
        fps: Number(this.fps()),
        transition: this.transition(),
        transitionSeconds: Number(this.transitionSeconds()),
        includeNarration: this.includeNarration(),
        includeSubtitles: this.includeSubtitles(),
        musicAssetId: this.music()?.id ?? null,
        musicVolume: Number(this.musicVolume()),
      });
      this.apply(e);
      this.toast.show('Export started');
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.exporting.set(false);
    }
  }

  protected async cancel(e: VideoExport): Promise<void> {
    try {
      this.apply(await this.api.cancelExport(e.id));
    } catch (err) {
      this.toast.error(errorMessage(err));
    }
  }

  private async load(projectId: string): Promise<void> {
    try {
      this.exports.set(await this.api.exports(projectId));
    } catch {
      // Exports list is optional; errors surface when the user exports.
    }
  }

  private apply(e: VideoExport): void {
    if (e.projectId !== this.project().id) return;
    const wasActive = this.exports().some((x) => x.id === e.id && !isTerminal(x.status));
    this.exports.update((list) => (list.some((x) => x.id === e.id) ? list.map((x) => (x.id === e.id ? e : x)) : [e, ...list]));
    if (wasActive && e.status === 'Completed') {
      this.toast.success('Export finished');
      this.exported.emit();
    }
  }
}
