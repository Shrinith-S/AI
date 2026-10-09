import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { ToastService } from '../core/app-state';
import { CatalogService } from '../core/catalog.service';
import { errorMessage } from '../core/errors';
import { Style } from '../core/models';
import { Icon } from '../shared/icon';

interface StyleForm {
  name: string;
  description: string;
  promptPrefix: string;
  promptSuffix: string;
  negativePrompt: string;
}

@Component({
  selector: 'app-styles',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Icon],
  template: `
    <div class="mx-auto max-w-6xl space-y-6">
      <div class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="text-2xl font-bold tracking-tight">Styles</h1>
          <p class="mt-1 text-sm text-muted">A style wraps your prompt: <span class="font-mono text-xs">prefix, your prompt, suffix</span> plus extra negative terms.</p>
        </div>
        <button type="button" class="btn btn-primary" (click)="edit(null)"><app-icon name="plus" [size]="16" /> New style</button>
      </div>

      @if (editingId() !== undefined) {
        <form class="card grid gap-4 p-5 md:grid-cols-2" (ngSubmit)="save()">
          <h2 class="font-semibold md:col-span-2">{{ editingId() ? 'Edit style' : 'Create style' }}</h2>
          <div><label class="label" for="sn">Name</label><input id="sn" name="sn" class="input" [(ngModel)]="form.name" maxlength="100" /></div>
          <div><label class="label" for="sd">Description</label><input id="sd" name="sd" class="input" [(ngModel)]="form.description" /></div>
          <div><label class="label" for="sp">Prompt prefix</label><input id="sp" name="sp" class="input" [(ngModel)]="form.promptPrefix" placeholder="cinematic film still" /></div>
          <div><label class="label" for="ss">Prompt suffix</label><input id="ss" name="ss" class="input" [(ngModel)]="form.promptSuffix" placeholder="film grain, dramatic lighting" /></div>
          <div class="md:col-span-2"><label class="label" for="sng">Negative prompt</label><input id="sng" name="sng" class="input" [(ngModel)]="form.negativePrompt" placeholder="cartoon, oversaturated" /></div>
          <p class="text-xs text-muted md:col-span-2">Preview: <span class="text-ink">{{ previewText() }}</span></p>
          <div class="flex gap-2 md:col-span-2">
            <button type="submit" class="btn btn-primary" [disabled]="!form.name.trim()"><app-icon name="check" [size]="16" /> Save</button>
            <button type="button" class="btn btn-ghost" (click)="editingId.set(undefined)">Cancel</button>
          </div>
        </form>
      }

      <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        @for (s of catalog.styles(); track s.id) {
          <div class="card flex flex-col p-4">
            <div class="flex items-start justify-between gap-2">
              <h3 class="font-semibold">{{ s.name }}</h3>
              @if (s.isBuiltIn) { <span class="badge bg-panel-2 text-muted">Built-in</span> }
            </div>
            @if (s.description) { <p class="mt-1 text-sm text-muted">{{ s.description }}</p> }
            <p class="mt-3 flex-1 text-xs text-muted">
              <span class="text-ink">{{ s.promptPrefix }}</span>{{ s.promptPrefix ? ', ' : '' }}<span class="italic">your prompt</span>{{ s.promptSuffix ? ', ' : '' }}<span class="text-ink">{{ s.promptSuffix }}</span>
            </p>
            <div class="mt-3 flex gap-1 border-t border-line pt-2">
              @if (s.isBuiltIn) {
                <button type="button" class="btn btn-ghost btn-sm" (click)="copy(s)"><app-icon name="copy" [size]="14" /> Customize a copy</button>
              } @else {
                <button type="button" class="btn btn-ghost btn-sm" (click)="edit(s)"><app-icon name="edit" [size]="14" /> Edit</button>
                <button type="button" class="btn btn-ghost btn-sm ml-auto hover:text-red-400" (click)="remove(s)"><app-icon name="trash" [size]="14" /></button>
              }
            </div>
          </div>
        }
      </div>
    </div>
  `,
})
export class StylesPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  protected readonly catalog = inject(CatalogService);

  /** undefined = editor closed, null = creating, id = editing. */
  protected readonly editingId = signal<string | null | undefined>(undefined);
  protected form: StyleForm = this.blank();

  ngOnInit(): void {
    void this.catalog.reload();
  }

  protected previewText(): string {
    return [this.form.promptPrefix, 'a lighthouse at dusk', this.form.promptSuffix].filter((p) => p.trim()).join(', ');
  }

  protected edit(s: Style | null): void {
    this.form = s
      ? { name: s.name, description: s.description ?? '', promptPrefix: s.promptPrefix ?? '', promptSuffix: s.promptSuffix ?? '', negativePrompt: s.negativePrompt ?? '' }
      : this.blank();
    this.editingId.set(s?.id ?? null);
  }

  protected copy(s: Style): void {
    this.edit(s);
    this.form.name = `${s.name} (custom)`;
    this.editingId.set(null);
  }

  protected async save(): Promise<void> {
    try {
      const id = this.editingId();
      if (id) await this.api.updateStyle(id, this.form);
      else await this.api.createStyle(this.form);
      await this.catalog.reload();
      this.editingId.set(undefined);
      this.toast.success('Style saved');
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  protected async remove(s: Style): Promise<void> {
    if (!confirm(`Delete style "${s.name}"?`)) return;
    try {
      await this.api.deleteStyle(s.id);
      await this.catalog.reload();
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  private blank(): StyleForm {
    return { name: '', description: '', promptPrefix: '', promptSuffix: '', negativePrompt: '' };
  }
}
