import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { CatalogService } from '../core/catalog.service';
import { errorMessage } from '../core/errors';
import { Icon } from '../shared/icon';
import { StyleCharacterFields } from '../shared/style-character-fields';

const EXAMPLE = 'A poor old man finds a bag of gold but becomes arrogant and eventually loses everything.';

@Component({
  selector: 'app-story-create',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, Icon, StyleCharacterFields],
  template: `
    <div class="mx-auto max-w-3xl">
      <h1 class="text-2xl font-bold tracking-tight">Story Video</h1>
      <p class="mt-1 text-muted">Describe a story. Chitra breaks it into scenes with image prompts, video prompts, camera moves and narration —
        you can edit every scene before anything is generated.</p>

      <form class="card mt-6 space-y-5 p-5" (ngSubmit)="create()">
        <div>
          <label class="label" for="title">Story title</label>
          <input id="title" name="title" class="input" [(ngModel)]="title" maxlength="200" placeholder="My Ramu Story" />
        </div>
        <div>
          <div class="mb-1.5 flex items-center justify-between">
            <label class="label !mb-0" for="desc">Story description</label>
            <button type="button" class="text-xs text-amber-500 hover:underline" (click)="description.set(example)">Use example</button>
          </div>
          <textarea id="desc" name="desc" class="input min-h-32" [(ngModel)]="description" maxlength="4000"
            placeholder="Who is it about, what happens, and how does it end?"></textarea>
        </div>
        <div class="grid gap-4 sm:grid-cols-2">
          <div>
            <label class="label" for="count">Scenes · {{ sceneCount() }}</label>
            <input id="count" name="count" type="range" min="3" max="12" class="w-full" [(ngModel)]="sceneCount" />
          </div>
          <div>
            <label class="label" for="dur">Seconds per scene</label>
            <select id="dur" name="dur" class="input" [(ngModel)]="duration">
              @for (d of [3, 4, 5, 6, 8, 10]; track d) { <option [ngValue]="d">{{ d }} sec</option> }
            </select>
          </div>
        </div>
        <div class="grid gap-4">
          <app-style-character-fields [(styleId)]="styleId" [(characterIds)]="characterIds" />
        </div>
        <p class="rounded-lg bg-panel-2 p-3 text-xs text-muted">
          The first selected character becomes the protagonist; their description is injected into every scene prompt to keep them consistent.
          The planner is rule-based (free, offline), so treat the scenes as a draft to refine.
        </p>
        @if (error()) {
          <p class="rounded-lg border border-red-500/30 bg-red-500/10 p-3 text-sm text-red-400" role="alert">{{ error() }}</p>
        }
        <div class="flex gap-2">
          <button type="submit" class="btn btn-primary" [disabled]="!title().trim() || !description().trim() || busy()">
            <app-icon name="book" [size]="16" /> {{ busy() ? 'Planning…' : 'Create scenes' }}
          </button>
          <a routerLink="/projects" class="btn btn-ghost">Open existing project</a>
        </div>
      </form>
    </div>
  `,
})
export class StoryCreatePage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly catalog = inject(CatalogService);

  protected readonly example = EXAMPLE;
  protected readonly title = signal('');
  protected readonly description = signal('');
  protected readonly sceneCount = signal(5);
  protected readonly duration = signal(5);
  protected readonly styleId = signal<string | null>(null);
  protected readonly characterIds = signal<string[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    void this.catalog.reload();
  }

  protected async create(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const project = await this.api.generateStory({
        title: this.title(),
        description: this.description(),
        sceneCount: Number(this.sceneCount()),
        sceneDurationSeconds: Number(this.duration()),
        styleId: this.styleId(),
        characterIds: this.characterIds(),
      });
      await this.router.navigate(['/projects', project.id]);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
