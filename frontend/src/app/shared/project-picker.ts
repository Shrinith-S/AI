import { ChangeDetectionStrategy, Component, OnInit, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { ToastService } from '../core/app-state';
import { errorMessage } from '../core/errors';
import { ProjectSummary } from '../core/models';
import { Icon } from './icon';

/** Lets the user choose (or create) a project to add an asset to. */
@Component({
  selector: 'app-project-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, FormsModule],
  host: { '(document:keydown.escape)': 'close.emit()' },
  template: `
    <div class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" (click)="close.emit()">
      <div class="card w-full max-w-md p-5" (click)="$event.stopPropagation()" role="dialog" aria-modal="true">
        <div class="mb-4 flex items-center justify-between">
          <h2 class="text-lg font-semibold">{{ title() }}</h2>
          <button type="button" class="btn-icon" (click)="close.emit()" aria-label="Close"><app-icon name="x" /></button>
        </div>
        @if (loading()) {
          <p class="text-sm text-muted">Loading projects…</p>
        } @else {
          <div class="max-h-72 space-y-1.5 overflow-y-auto">
            @for (p of projects(); track p.id) {
              <button type="button" class="flex w-full items-center gap-3 rounded-lg border border-line p-2.5 text-left hover:border-amber-500/60" (click)="picked.emit(p.id)">
                <app-icon name="folder" class="text-amber-500" />
                <span class="flex-1 truncate text-sm">{{ p.name }}</span>
                <span class="text-xs text-muted">{{ p.assetCount }} assets</span>
              </button>
            } @empty {
              <p class="text-sm text-muted">No projects yet. Create one below.</p>
            }
          </div>
          <form class="mt-4 flex gap-2 border-t border-line pt-4" (ngSubmit)="create()">
            <input class="input" name="name" [(ngModel)]="newName" placeholder="New project name" maxlength="200" />
            <button type="submit" class="btn btn-primary" [disabled]="!newName().trim()"><app-icon name="plus" [size]="16" /> Create</button>
          </form>
        }
      </div>
    </div>
  `,
})
export class ProjectPicker implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly title = input('Add to project');
  readonly picked = output<string>();
  readonly close = output<void>();

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
      this.picked.emit(project.id);
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }
}
