import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { Generation, isTerminal } from '../core/models';
import { AgoPipe, statusClass, typeLabel } from './format';
import { Icon } from './icon';

/** Thumbnail tile for a generation with live progress and quick actions. */
@Component({
  selector: 'app-generation-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, AgoPipe, DecimalPipe],
  template: `
    @let g = generation();
    <div class="card group overflow-hidden">
      <button type="button" class="relative block aspect-square w-full overflow-hidden bg-panel-2" (click)="view.emit(g)"
        [attr.aria-label]="'View ' + g.prompt">
        @if (output(); as o) {
          @if (o.type === 'Video') {
            @if (o.thumbnailUrl) {
              <img [src]="o.thumbnailUrl" class="h-full w-full object-cover" loading="lazy" alt="" />
            } @else {
              <video [src]="o.url + '#t=0.1'" class="h-full w-full object-cover" preload="metadata" muted playsinline></video>
            }
            <span class="badge absolute bottom-2 left-2 bg-black/60 text-white">
              <app-icon name="play" [size]="11" /> {{ o.durationSeconds ? (o.durationSeconds | number: '1.0-1') + 's' : 'video' }}
            </span>
          } @else {
            <img [src]="o.thumbnailUrl ?? o.url" class="h-full w-full object-cover transition duration-300 group-hover:scale-[1.03]" loading="lazy" alt="" />
          }
        } @else if (active()) {
          <div class="flex h-full flex-col items-center justify-center gap-3 p-4 text-center">
            <div class="h-8 w-8 animate-spin rounded-full border-2 border-amber-500/30 border-t-amber-500"></div>
            <div class="w-full max-w-40">
              <div class="h-1.5 overflow-hidden rounded-full bg-line">
                <div class="h-full rounded-full bg-gradient-to-r from-amber-500 to-rose-500 transition-all" [style.width.%]="g.progress"></div>
              </div>
              <p class="mt-2 text-xs text-muted">{{ g.statusMessage ?? g.status }} · {{ g.progress }}%</p>
            </div>
          </div>
        } @else if (g.status === 'Failed') {
          <div class="flex h-full flex-col items-center justify-center gap-2 p-4 text-center text-red-400">
            <app-icon name="alert" [size]="24" />
            <p class="line-clamp-4 text-xs">{{ g.errorMessage }}</p>
          </div>
        } @else {
          <div class="flex h-full flex-col items-center justify-center gap-1 text-muted">
            <app-icon name="x" [size]="22" /><span class="text-xs">{{ g.status }}</span>
          </div>
        }
        @if (g.outputs.length > 1) {
          <span class="badge absolute right-2 top-2 bg-black/60 text-white"><app-icon name="layers" [size]="11" /> {{ g.outputs.length }}</span>
        }
      </button>
      <div class="space-y-2 p-3">
        <p class="line-clamp-2 min-h-[2.5rem] text-sm" [title]="g.prompt">{{ g.prompt }}</p>
        <div class="flex flex-wrap items-center gap-1.5 text-[11px] text-muted">
          <span class="badge" [class]="statusCls()">{{ g.status }}</span>
          <span class="badge bg-panel-2">{{ label() }}</span>
          <span class="max-w-[8rem] truncate">{{ g.modelId }}</span>
          <span class="ml-auto">{{ g.createdAt | ago }}</span>
        </div>
        @if (showActions()) {
          <div class="flex items-center gap-0.5 border-t border-line pt-2">
            @if (output(); as o) {
              <a class="btn-icon" [href]="o.downloadUrl" title="Download" download><app-icon name="download" [size]="16" /></a>
            }
            @if (terminal()) {
              <button type="button" class="btn-icon" title="Regenerate (same seed)" (click)="regenerate.emit(g)"><app-icon name="refresh" [size]="16" /></button>
              <button type="button" class="btn-icon" title="Create variation (new seed)" (click)="variation.emit(g)"><app-icon name="shuffle" [size]="16" /></button>
            } @else {
              <button type="button" class="btn-icon" title="Cancel" (click)="cancel.emit(g)"><app-icon name="stop" [size]="16" /></button>
            }
            <button type="button" class="btn-icon" title="Use in project" (click)="useInProject.emit(g)"><app-icon name="folder" [size]="16" /></button>
            <button type="button" class="btn-icon ml-auto hover:text-red-400" title="Delete" (click)="remove.emit(g)"><app-icon name="trash" [size]="16" /></button>
          </div>
        }
      </div>
    </div>
  `,
})
export class GenerationCard {
  readonly generation = input.required<Generation>();
  readonly showActions = input(true);

  readonly view = output<Generation>();
  readonly regenerate = output<Generation>();
  readonly variation = output<Generation>();
  readonly cancel = output<Generation>();
  readonly remove = output<Generation>();
  readonly useInProject = output<Generation>();

  protected readonly output = computed(() => this.generation().outputs[0] ?? null);
  protected readonly active = computed(() => !isTerminal(this.generation().status));
  protected readonly terminal = computed(() => isTerminal(this.generation().status));
  protected readonly statusCls = computed(() => statusClass[this.generation().status]);
  protected readonly label = computed(() => typeLabel[this.generation().type]);
}
