import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { ToastService } from '../core/app-state';
import { errorMessage } from '../core/errors';
import { ProjectSummary } from '../core/models';
import { AgoPipe } from '../shared/format';
import { Icon } from '../shared/icon';

@Component({
  selector: 'app-projects',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, Icon, AgoPipe],
  template: `
    <div class="mx-auto max-w-7xl">
      <div class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-bold tracking-tight">Projects</h1>
          <p class="mt-1 text-sm text-muted">Organize characters, scenes, images, videos, audio and final exports.</p>
        </div>
        <form class="flex gap-2" (ngSubmit)="create()">
          <input class="input w-56" name="name" [(ngModel)]="newName" placeholder="New project name" maxlength="200" />
          <button type="submit" class="btn btn-primary" [disabled]="!newName().trim()"><app-icon name="plus" [size]="16" /> Create</button>
          <a routerLink="/create/story" class="btn btn-secondary"><app-icon name="book" [size]="16" /> From story</a>
        </form>
      </div>

      <div class="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
        @for (p of projects(); track p.id) {
          <a [routerLink]="['/projects', p.id]" class="card group overflow-hidden transition hover:border-amber-500/50">
            <div class="aspect-video bg-panel-2">
              @if (p.coverUrl) {
                <img [src]="p.coverUrl" class="h-full w-full object-cover" loading="lazy" alt="" />
              } @else {
                <div class="flex h-full items-center justify-center text-muted"><app-icon name="folder" [size]="32" /></div>
              }
            </div>
            <div class="p-4">
              <h2 class="truncate font-semibold">{{ p.name }}</h2>
              @if (p.description) { <p class="mt-0.5 line-clamp-1 text-sm text-muted">{{ p.description }}</p> }
              <p class="mt-2 text-xs text-muted">{{ p.sceneCount }} scenes · {{ p.characterCount }} characters · {{ p.assetCount }} assets · {{ p.updatedAt | ago }}</p>
            </div>
          </a>
        } @empty {
          @if (!loading()) {
            <div class="col-span-full rounded-xl border border-dashed border-line p-12 text-center text-sm text-muted">
              No projects yet. Create one, or start from a story.
            </div>
          }
        }
      </div>
    </div>
  `,
})
export class ProjectsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  protected readonly projects = signal<ProjectSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly newName = signal('');

  async ngOnInit(): Promise<void> {
    try {
      this.projects.set(await this.api.projects());
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  protected async create(): Promise<void> {
    try {
      const project = await this.api.createProject({ name: this.newName().trim() });
      await this.router.navigate(['/projects', project.id]);
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }
}
