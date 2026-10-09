import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { ToastService } from '../core/app-state';
import { CatalogService } from '../core/catalog.service';
import { errorMessage } from '../core/errors';
import { GenerationStore } from '../core/generation-store';
import { Character, MediaAsset } from '../core/models';
import { Icon } from '../shared/icon';
import { ImagePicker } from '../shared/image-picker';

interface CharacterForm {
  name: string;
  description: string;
  age: number | null;
  gender: string;
  clothing: string;
  appearance: string;
  personality: string;
  promptOverride: string;
  preferredSeed: number | null;
}

const EMPTY: CharacterForm = { name: '', description: '', age: null, gender: '', clothing: '', appearance: '', personality: '', promptOverride: '', preferredSeed: null };

@Component({
  selector: 'app-characters',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Icon, ImagePicker],
  template: `
    <div class="mx-auto grid max-w-7xl gap-6 lg:grid-cols-[18rem_minmax(0,1fr)]">
      <aside class="space-y-2">
        <div class="flex items-center justify-between">
          <h1 class="text-2xl font-bold tracking-tight">Characters</h1>
          <button type="button" class="btn btn-primary btn-sm" (click)="select(null)"><app-icon name="plus" [size]="14" /> New</button>
        </div>
        <p class="text-sm text-muted">Reusable people and creatures. Their description is injected into every prompt that uses them.</p>
        <div class="space-y-1.5 pt-2">
          @for (c of catalog.characters(); track c.id) {
            <button type="button" class="flex w-full items-center gap-3 rounded-lg border p-2 text-left transition"
              [class]="selectedId() === c.id ? 'border-amber-500/60 bg-amber-500/5' : 'border-line hover:border-muted/60'" (click)="select(c)">
              <span class="h-10 w-10 shrink-0 overflow-hidden rounded-md bg-panel-2">
                @if (c.referenceAsset) { <img [src]="c.referenceAsset.thumbnailUrl ?? c.referenceAsset.url" class="h-full w-full object-cover" alt="" /> }
              </span>
              <span class="min-w-0">
                <span class="block truncate text-sm font-medium">{{ c.name }}</span>
                <span class="block truncate text-xs text-muted">{{ c.generatedPrompt }}</span>
              </span>
            </button>
          } @empty {
            <p class="text-sm text-muted">No characters yet.</p>
          }
        </div>
      </aside>

      <form class="card space-y-5 p-5" (ngSubmit)="save()">
        <h2 class="text-lg font-semibold">{{ selectedId() ? 'Edit character' : 'Create character' }}</h2>
        <div class="grid gap-4 md:grid-cols-[minmax(0,1fr)_16rem]">
          <div class="grid gap-4 sm:grid-cols-2">
            <div class="sm:col-span-2"><label class="label" for="cn">Name</label><input id="cn" name="cn" class="input" [(ngModel)]="form.name" (ngModelChange)="touch()" maxlength="100" placeholder="Ramu" /></div>
            <div><label class="label" for="ca">Age</label><input id="ca" name="ca" type="number" min="0" max="150" class="input" [(ngModel)]="form.age" (ngModelChange)="touch()" /></div>
            <div><label class="label" for="cg">Gender</label><input id="cg" name="cg" class="input" [(ngModel)]="form.gender" (ngModelChange)="touch()" placeholder="man / woman / boy / …" /></div>
            <div class="sm:col-span-2"><label class="label" for="cap">Appearance</label><input id="cap" name="cap" class="input" [(ngModel)]="form.appearance" (ngModelChange)="touch()" placeholder="Indian man, grey beard, thin face, kind eyes" /></div>
            <div class="sm:col-span-2"><label class="label" for="ccl">Clothing</label><input id="ccl" name="ccl" class="input" [(ngModel)]="form.clothing" (ngModelChange)="touch()" placeholder="a simple beige kurta and worn sandals" /></div>
            <div class="sm:col-span-2"><label class="label" for="cd">Description</label><textarea id="cd" name="cd" class="input min-h-16" [(ngModel)]="form.description" (ngModelChange)="touch()" placeholder="Background, role in the story…"></textarea></div>
            <div class="sm:col-span-2"><label class="label" for="cp">Personality <span class="normal-case">(not sent to image models)</span></label><input id="cp" name="cp" class="input" [(ngModel)]="form.personality" (ngModelChange)="touch()" placeholder="humble, later proud and arrogant" /></div>
          </div>
          <div>
            <span class="label">Reference image</span>
            <app-image-picker [value]="reference()" (changed)="reference.set($event); touch()" />
          </div>
        </div>

        <div class="rounded-lg border border-line bg-panel-2 p-4">
          <p class="label">Reusable prompt</p>
          <p class="text-sm">{{ preview() || 'Fill in the fields above.' }}</p>
          <details class="mt-3">
            <summary class="cursor-pointer text-xs text-muted">Write the prompt yourself / fixed seed</summary>
            <div class="mt-3 grid gap-3 sm:grid-cols-[minmax(0,1fr)_10rem]">
              <textarea name="po" class="input min-h-16" [(ngModel)]="form.promptOverride" (ngModelChange)="touch()" placeholder="Overrides the generated description"></textarea>
              <div>
                <input name="seed" type="number" class="input" [(ngModel)]="form.preferredSeed" (ngModelChange)="touch()" placeholder="Seed" />
                <p class="mt-1 text-[11px] text-muted">Reused when a generation's seed is random — helps keep the face similar.</p>
              </div>
            </div>
          </details>
        </div>

        <div class="flex flex-wrap gap-2">
          <button type="submit" class="btn btn-primary" [disabled]="!form.name.trim() || saving()"><app-icon name="check" [size]="16" /> {{ saving() ? 'Saving…' : 'Save character' }}</button>
          @if (selectedId()) {
            <button type="button" class="btn btn-secondary" [disabled]="portraitBusy()" (click)="generatePortrait()">
              <app-icon name="sparkles" [size]="16" /> {{ portraitBusy() ? 'Generating portrait… ' + (portrait()?.progress ?? 0) + '%' : 'Generate portrait' }}
            </button>
            <button type="button" class="btn btn-danger ml-auto" (click)="remove()"><app-icon name="trash" [size]="16" /> Delete</button>
          }
        </div>

        @if (portrait(); as g) {
          @if (g.status === 'Completed' && g.outputs[0]; as o) {
            <div class="flex items-end gap-3">
              <img [src]="o.url" class="h-40 rounded-lg object-cover" alt="Generated portrait" />
              <button type="button" class="btn btn-secondary btn-sm" (click)="useAsReference(o)"><app-icon name="check" [size]="14" /> Use as reference image</button>
            </div>
          } @else if (g.status === 'Failed') {
            <p class="text-sm text-red-400">{{ g.errorMessage }}</p>
          }
        }
      </form>
    </div>
  `,
})
export class CharactersPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly store = inject(GenerationStore);
  protected readonly catalog = inject(CatalogService);

  protected readonly selectedId = signal<string | null>(null);
  protected readonly reference = signal<MediaAsset | null>(null);
  protected readonly saving = signal(false);
  protected form: CharacterForm = { ...EMPTY };
  private readonly edits = signal(0);

  protected readonly portraitId = signal<string | null>(null);
  protected readonly portrait = this.store.watch(this.portraitId);
  protected readonly portraitBusy = computed(() => {
    const g = this.portrait();
    return !!g && (g.status === 'Queued' || g.status === 'Processing');
  });

  /** Mirrors Character.BuildPrompt() on the server so the user sees the result while typing. */
  protected readonly preview = computed(() => {
    this.edits();
    const f = this.form;
    if (f.promptOverride.trim()) return `${f.name.trim()}, ${f.promptOverride.trim()}`;
    const identity = [f.age ? `${f.age}-year-old` : '', f.gender.trim().toLowerCase()].filter(Boolean).join(' ');
    const parts = [f.name.trim(), identity, f.appearance.trim(), f.clothing.trim() ? `wearing ${f.clothing.trim()}` : '', !f.appearance.trim() ? f.description.trim() : '']
      .map((p) => p.replace(/\.$/, ''))
      .filter(Boolean);
    return parts.join(', ');
  });

  ngOnInit(): void {
    void this.catalog.reload();
  }

  protected touch(): void {
    this.edits.update((v) => v + 1);
  }

  protected select(c: Character | null): void {
    this.selectedId.set(c?.id ?? null);
    this.reference.set(c?.referenceAsset ?? null);
    this.portraitId.set(null);
    this.form = c
      ? {
          name: c.name, description: c.description ?? '', age: c.age, gender: c.gender ?? '', clothing: c.clothing ?? '',
          appearance: c.appearance ?? '', personality: c.personality ?? '', promptOverride: c.promptOverride ?? '', preferredSeed: c.preferredSeed,
        }
      : { ...EMPTY };
    this.touch();
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    try {
      const body = { ...this.form, age: this.form.age ? Number(this.form.age) : null, preferredSeed: this.form.preferredSeed ?? null, referenceAssetId: this.reference()?.id ?? null };
      const id = this.selectedId();
      const saved = id ? await this.api.updateCharacter(id, body) : await this.api.createCharacter(body);
      await this.catalog.reload();
      this.select(saved);
      this.toast.success('Character saved');
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.saving.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const id = this.selectedId();
    if (!id || !confirm('Delete this character? Scenes keep their prompts but lose the link.')) return;
    try {
      await this.api.deleteCharacter(id);
      await this.catalog.reload();
      this.select(null);
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  protected async generatePortrait(): Promise<void> {
    const id = this.selectedId();
    if (!id) return;
    try {
      const g = await this.api.createImage({
        prompt: `Portrait of ${this.form.name.trim()}, looking at the camera, neutral background, soft studio lighting, highly detailed face`,
        characterIds: [id],
        width: 832,
        height: 1216,
      });
      this.store.upsert(g);
      this.portraitId.set(g.id);
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  protected async useAsReference(asset: MediaAsset): Promise<void> {
    this.reference.set(asset);
    await this.save();
  }
}
