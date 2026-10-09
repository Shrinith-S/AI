import { ChangeDetectionStrategy, Component, inject, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CatalogService } from '../core/catalog.service';
import { Icon } from './icon';

/** Style dropdown plus character chips; selected characters are injected into the prompt for consistency. */
@Component({
  selector: 'app-style-character-fields',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, Icon],
  template: `
    <div>
      <label class="label" for="style-select">Style</label>
      <select id="style-select" class="input" [ngModel]="styleId() ?? ''" [ngModelOptions]="{ standalone: true }" (ngModelChange)="styleId.set($event || null)">
        <option value="">None</option>
        @for (s of catalog.styles(); track s.id) {
          <option [value]="s.id">{{ s.name }}</option>
        }
      </select>
    </div>
    <div>
      <div class="mb-1.5 flex items-center justify-between">
        <span class="label !mb-0">Characters</span>
        <a routerLink="/characters" class="text-[11px] text-amber-500 hover:underline">Manage</a>
      </div>
      <div class="flex flex-wrap gap-1.5">
        @for (c of catalog.characters(); track c.id) {
          <button type="button" class="chip" [class.chip-active]="characterIds().includes(c.id)" (click)="toggle(c.id)" [title]="c.generatedPrompt">
            @if (characterIds().includes(c.id)) { <app-icon name="check" [size]="12" /> }
            {{ c.name }}
          </button>
        } @empty {
          <p class="text-xs text-muted">No characters yet. Create one to keep a person consistent across generations.</p>
        }
      </div>
    </div>
  `,
  host: { class: 'contents' },
})
export class StyleCharacterFields {
  protected readonly catalog = inject(CatalogService);
  readonly styleId = model<string | null>(null);
  readonly characterIds = model<string[]>([]);

  protected toggle(id: string): void {
    this.characterIds.update((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]));
  }
}
