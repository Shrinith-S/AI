import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { ApiService } from '../core/api.service';
import { ToastService } from '../core/app-state';
import { errorMessage } from '../core/errors';
import { MediaAsset } from '../core/models';
import { Icon } from './icon';

/** Upload an image (click or drag & drop) or pick one of the recent images. */
@Component({
  selector: 'app-image-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    @if (value(); as v) {
      <div class="relative overflow-hidden rounded-lg border border-line bg-black">
        <img [src]="v.url" class="max-h-64 w-full object-contain" alt="Selected input image" />
        <button type="button" class="btn btn-secondary btn-sm absolute right-2 top-2" (click)="changed.emit(null)">
          <app-icon name="x" [size]="14" /> Change
        </button>
        @if (v.width) {
          <span class="badge absolute bottom-2 left-2 bg-black/60 text-white">{{ v.width }}×{{ v.height }}</span>
        }
      </div>
    } @else {
      <label class="flex cursor-pointer flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed border-line bg-panel-2 px-4 py-8 text-center text-sm text-muted transition hover:border-amber-500/60"
        [class.border-amber-500]="dragging()"
        (dragover)="$event.preventDefault(); dragging.set(true)" (dragleave)="dragging.set(false)" (drop)="onDrop($event)">
        @if (uploading()) {
          <div class="h-6 w-6 animate-spin rounded-full border-2 border-amber-500/30 border-t-amber-500"></div>
          Uploading…
        } @else {
          <app-icon name="upload" [size]="22" />
          <span><span class="font-medium text-ink">Upload image</span> or drag & drop</span>
          <span class="text-xs">PNG, JPG or WEBP</span>
        }
        <input type="file" accept="image/png,image/jpeg,image/webp" class="hidden" (change)="onFile($event)" />
      </label>
      <button type="button" class="btn btn-ghost btn-sm mt-2" (click)="toggleRecent()">
        <app-icon name="image" [size]="14" /> {{ showRecent() ? 'Hide' : 'Choose from' }} recent images
      </button>
      @if (showRecent()) {
        <div class="mt-2 grid grid-cols-4 gap-2">
          @for (a of recent(); track a.id) {
            <button type="button" class="aspect-square overflow-hidden rounded-md border border-line hover:border-amber-500" (click)="changed.emit(a)">
              <img [src]="a.thumbnailUrl ?? a.url" class="h-full w-full object-cover" loading="lazy" alt="" />
            </button>
          } @empty {
            <p class="col-span-4 text-xs text-muted">No images yet.</p>
          }
        </div>
      }
    }
  `,
})
export class ImagePicker {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly value = input<MediaAsset | null>(null);
  readonly projectId = input<string | null>(null);
  readonly changed = output<MediaAsset | null>();

  protected readonly uploading = signal(false);
  protected readonly dragging = signal(false);
  protected readonly showRecent = signal(false);
  protected readonly recent = signal<MediaAsset[]>([]);

  protected onFile(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) void this.upload(file);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file) void this.upload(file);
  }

  protected async toggleRecent(): Promise<void> {
    this.showRecent.update((v) => !v);
    if (this.showRecent()) {
      try {
        this.recent.set((await this.api.media({ type: 'Image', pageSize: 24 })).items);
      } catch (e) {
        this.toast.error(errorMessage(e));
      }
    }
  }

  private async upload(file: File): Promise<void> {
    this.uploading.set(true);
    try {
      this.changed.emit(await this.api.upload(file, this.projectId()));
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.uploading.set(false);
    }
  }
}
