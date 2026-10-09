import { ChangeDetectionStrategy, Component, inject, input, model, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { ToastService } from '../core/app-state';
import { errorMessage } from '../core/errors';
import { GenerationType, PromptEnhancement } from '../core/models';
import { Icon } from './icon';

/** Prompt textarea with the optional, free rule-based enhancer. */
@Component({
  selector: 'app-prompt-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Icon],
  template: `
    <div>
      <div class="mb-1.5 flex items-center justify-between">
        <label class="label !mb-0" [for]="id">{{ label() }}</label>
        <button type="button" class="btn btn-ghost btn-sm -mr-2" [disabled]="!value().trim() || enhancing()" (click)="enhance()"
          title="Expand into a detailed prompt (local rule-based, no API cost)">
          <app-icon name="wand" [size]="14" /> {{ enhancing() ? 'Enhancing…' : 'Enhance' }}
        </button>
      </div>
      <textarea [id]="id" class="input min-h-28 resize-y leading-relaxed" [(ngModel)]="value" [ngModelOptions]="{ standalone: true }" [placeholder]="placeholder()" maxlength="4000"
        (keydown.control.enter)="submit()" (keydown.meta.enter)="submit()"></textarea>
      <div class="mt-1 flex justify-between text-[11px] text-muted">
        <span>Ctrl+Enter to generate</span><span>{{ value().length }}/4000</span>
      </div>

      @if (suggestion(); as s) {
        <div class="mt-2 rounded-lg border border-amber-500/30 bg-amber-500/5 p-3 text-sm">
          <p class="mb-1 text-xs font-medium text-amber-500">Enhanced prompt ({{ s.enhancer }})</p>
          <p class="leading-relaxed">{{ s.prompt }}</p>
          <div class="mt-2 flex gap-2">
            <button type="button" class="btn btn-primary btn-sm" (click)="accept(s)"><app-icon name="check" [size]="14" /> Use</button>
            <button type="button" class="btn btn-secondary btn-sm" (click)="enhance(variation + 1)"><app-icon name="shuffle" [size]="14" /> Another</button>
            <button type="button" class="btn btn-ghost btn-sm" (click)="suggestion.set(null)">Dismiss</button>
          </div>
        </div>
      }
    </div>
  `,
})
export class PromptField {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly value = model('');
  readonly type = input<GenerationType>('TextToImage');
  readonly styleHint = input<string | null>(null);
  readonly label = input('Prompt');
  readonly placeholder = input('Describe what you want to see…');
  /** Optional callback for Ctrl+Enter. */
  readonly onSubmit = input<(() => void) | null>(null);

  protected readonly id = `prompt-${Math.random().toString(36).slice(2, 8)}`;
  protected readonly enhancing = signal(false);
  protected readonly suggestion = signal<PromptEnhancement | null>(null);
  protected variation = 0;

  protected async enhance(variation = 0): Promise<void> {
    this.variation = variation;
    this.enhancing.set(true);
    try {
      const source = this.suggestion()?.original ?? this.value();
      this.suggestion.set(await this.api.enhancePrompt(source, this.type(), this.styleHint(), variation));
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.enhancing.set(false);
    }
  }

  protected accept(s: PromptEnhancement): void {
    this.value.set(s.prompt);
    this.suggestion.set(null);
  }

  protected submit(): void {
    this.onSubmit()?.();
  }
}
