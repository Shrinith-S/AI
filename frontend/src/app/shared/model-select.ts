import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AiModel } from '../core/models';
import { formatBytes } from './format';

/** Model dropdown that shows install state and hardware fit for the selected model. */
@Component({
  selector: 'app-model-select',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  template: `
    <label class="label" for="model-select">Model</label>
    <select id="model-select" class="input" [(ngModel)]="value" [ngModelOptions]="{ standalone: true }">
      @for (m of models(); track m.id) {
        <option [value]="m.id">{{ m.name }}{{ m.installState === 'Installed' || m.installState === 'OnDemand' ? '' : m.installState === 'Unknown' ? '' : ' — not installed' }}</option>
      }
    </select>
    @if (selected(); as m) {
      <div class="mt-1.5 flex flex-wrap items-center gap-1.5 text-[11px]">
        <span class="badge" [class]="installClass()">{{ installLabel() }}</span>
        <span class="badge" [class]="readinessClass()" [title]="m.readinessReason">{{ m.readiness }}</span>
        <span class="text-muted">{{ size() }} · {{ m.minVramGb }}+ GB VRAM</span>
        @if (m.installState === 'NotInstalled' || m.installState === 'Partial') {
          <a routerLink="/models" class="text-amber-500 hover:underline">Install…</a>
        }
      </div>
      @if (m.notes) {
        <p class="mt-1 text-[11px] text-muted">{{ m.notes }}</p>
      }
    }
  `,
})
export class ModelSelect {
  readonly models = input.required<AiModel[]>();
  readonly value = model<string>('');

  protected readonly selected = computed(() => this.models().find((m) => m.id === this.value()) ?? null);
  protected readonly size = computed(() => formatBytes(this.selected()?.totalSizeBytes));
  protected readonly installLabel = computed(() => {
    const state = this.selected()?.installState;
    return state === 'Installed' ? 'Installed' : state === 'OnDemand' ? 'Downloaded per Kaggle run' : state === 'Partial' ? 'Partially installed' : state === 'NotInstalled' ? 'Not installed' : 'Install state unknown';
  });
  protected readonly installClass = computed(() => {
    const state = this.selected()?.installState;
    return state === 'Installed' ? 'bg-emerald-500/15 text-emerald-500' : state === 'OnDemand' ? 'bg-sky-500/15 text-sky-400' : state === 'Unknown' ? 'bg-panel-2 text-muted' : 'bg-red-500/15 text-red-400';
  });
  protected readonly readinessClass = computed(() => {
    const r = this.selected()?.readiness;
    return r === 'Recommended' ? 'bg-emerald-500/15 text-emerald-500' : r === 'Limited' ? 'bg-amber-500/15 text-amber-500' : 'bg-red-500/15 text-red-400';
  });
}
