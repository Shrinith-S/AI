import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { Generation, isTerminal } from '../core/models';
import { statusClass, typeLabel } from './format';
import { Icon } from './icon';

/** Modal with the full-size output and every parameter used, so results can be reproduced. */
@Component({
  selector: 'app-generation-viewer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, DatePipe, DecimalPipe],
  host: { '(document:keydown.escape)': 'close.emit()' },
  template: `
    @let g = generation();
    <div class="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-3 backdrop-blur-sm" (click)="close.emit()">
      <div class="card flex max-h-[92vh] w-full max-w-6xl flex-col overflow-hidden lg:flex-row" (click)="$event.stopPropagation()" role="dialog" aria-modal="true">
        <div class="flex min-h-[40vh] flex-1 items-center justify-center bg-black">
          @if (current(); as o) {
            @if (o.type === 'Video') {
              <video [src]="o.url" controls autoplay loop playsinline class="max-h-[85vh] max-w-full"></video>
            } @else {
              <img [src]="o.url" class="max-h-[85vh] max-w-full object-contain" alt="" />
            }
          } @else if (g.status === 'Failed') {
            <div class="max-w-md p-6 text-center text-red-400"><app-icon name="alert" [size]="28" /><p class="mt-2 text-sm">{{ g.errorMessage }}</p></div>
          } @else {
            <div class="p-6 text-center text-muted">
              <p class="text-sm">{{ g.statusMessage ?? g.status }} · {{ g.progress }}%</p>
              <div class="mx-auto mt-3 h-1.5 w-56 overflow-hidden rounded-full bg-line">
                <div class="h-full bg-gradient-to-r from-amber-500 to-rose-500" [style.width.%]="g.progress"></div>
              </div>
            </div>
          }
        </div>
        <aside class="w-full space-y-4 overflow-y-auto border-line p-5 text-sm lg:w-96 lg:border-l">
          <div class="flex items-start justify-between gap-2">
            <div class="flex flex-wrap gap-1.5">
              <span class="badge" [class]="statusCls()">{{ g.status }}</span>
              <span class="badge bg-panel-2">{{ label() }}</span>
            </div>
            <button type="button" class="btn-icon -mr-2 -mt-2" (click)="close.emit()" aria-label="Close"><app-icon name="x" /></button>
          </div>

          @if (g.outputs.length > 1) {
            <div class="flex gap-2 overflow-x-auto">
              @for (o of g.outputs; track o.id; let i = $index) {
                <button type="button" class="h-14 w-14 shrink-0 overflow-hidden rounded-md border-2"
                  [class]="i === index() ? 'border-amber-500' : 'border-transparent'" (click)="index.set(i)">
                  <img [src]="o.thumbnailUrl ?? o.url" class="h-full w-full object-cover" alt="" />
                </button>
              }
            </div>
          }

          <section>
            <h3 class="label">Prompt</h3>
            <p class="whitespace-pre-wrap">{{ g.prompt }}</p>
          </section>
          @if (g.finalPrompt !== g.prompt) {
            <section>
              <h3 class="label">Prompt sent to model</h3>
              <p class="whitespace-pre-wrap text-muted">{{ g.finalPrompt }}</p>
            </section>
          }
          @if (g.negativePrompt) {
            <section>
              <h3 class="label">Negative prompt</h3>
              <p class="text-muted">{{ g.negativePrompt }}</p>
            </section>
          }
          @if (g.warnings.length) {
            <section class="rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-xs text-amber-500">
              @for (w of g.warnings; track w) { <p>{{ w }}</p> }
            </section>
          }

          <dl class="grid grid-cols-2 gap-x-3 gap-y-2 text-xs">
            <dt class="text-muted">Model</dt><dd class="truncate">{{ g.modelId }}</dd>
            <dt class="text-muted">Engine</dt><dd>{{ g.providerName }}</dd>
            <dt class="text-muted">Size</dt><dd>{{ g.width }}×{{ g.height }}</dd>
            <dt class="text-muted">Seed</dt><dd class="font-mono">{{ g.seed }}</dd>
            <dt class="text-muted">Steps / CFG</dt><dd>{{ g.steps }} / {{ g.cfgScale }}</dd>
            @if (g.sampler) { <dt class="text-muted">Sampler</dt><dd>{{ g.sampler }} · {{ g.scheduler }}</dd> }
            @if (g.frameCount) {
              <dt class="text-muted">Frames / FPS</dt><dd>{{ g.frameCount }} @ {{ g.fps }} fps (~{{ g.durationSeconds | number: '1.0-1' }}s)</dd>
              <dt class="text-muted">Motion</dt><dd>{{ g.motionStrength }}{{ g.cameraMovement ? ' · ' + g.cameraMovement : '' }}</dd>
            }
            <dt class="text-muted">Created</dt><dd>{{ g.createdAt | date: 'medium' }}</dd>
            @if (g.completedAt && g.startedAt) {
              <dt class="text-muted">Run time</dt><dd>{{ runSeconds() | number: '1.0-0' }}s</dd>
            }
          </dl>

          <div class="flex flex-wrap gap-2 border-t border-line pt-4">
            @if (current(); as o) {
              <a class="btn btn-secondary btn-sm" [href]="o.downloadUrl" download><app-icon name="download" [size]="14" /> Download</a>
            }
            @if (terminal()) {
              <button type="button" class="btn btn-secondary btn-sm" (click)="regenerate.emit(g)"><app-icon name="refresh" [size]="14" /> Regenerate</button>
              <button type="button" class="btn btn-secondary btn-sm" (click)="variation.emit(g)"><app-icon name="shuffle" [size]="14" /> Variation</button>
            }
            @if (current()?.type === 'Image') {
              <button type="button" class="btn btn-secondary btn-sm" (click)="animate.emit(current()!.id)"><app-icon name="video" [size]="14" /> Animate</button>
            }
            <button type="button" class="btn btn-secondary btn-sm" (click)="useInProject.emit(g)"><app-icon name="folder" [size]="14" /> Use in project</button>
            <button type="button" class="btn btn-danger btn-sm" (click)="remove.emit(g)"><app-icon name="trash" [size]="14" /> Delete</button>
          </div>
        </aside>
      </div>
    </div>
  `,
})
export class GenerationViewer {
  readonly generation = input.required<Generation>();
  readonly close = output<void>();
  readonly regenerate = output<Generation>();
  readonly variation = output<Generation>();
  readonly remove = output<Generation>();
  readonly useInProject = output<Generation>();
  readonly animate = output<string>();

  protected readonly index = signal(0);
  protected readonly current = computed(() => this.generation().outputs[this.index()] ?? this.generation().outputs[0] ?? null);
  protected readonly terminal = computed(() => isTerminal(this.generation().status));
  protected readonly statusCls = computed(() => statusClass[this.generation().status]);
  protected readonly label = computed(() => typeLabel[this.generation().type]);
  protected readonly runSeconds = computed(() => {
    const g = this.generation();
    return g.startedAt && g.completedAt ? (new Date(g.completedAt).getTime() - new Date(g.startedAt).getTime()) / 1000 : 0;
  });
}
