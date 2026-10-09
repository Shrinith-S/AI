import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { ToastService } from '../core/app-state';
import { errorMessage } from '../core/errors';
import { GenerationStore } from '../core/generation-store';
import { Generation } from '../core/models';
import { GenerationActions } from '../shared/generation-actions';
import { GenerationCard } from '../shared/generation-card';
import { Icon } from '../shared/icon';

const FILTERS = [
  { key: '', label: 'All' },
  { key: 'images', label: 'Images' },
  { key: 'videos', label: 'Videos' },
  { key: 'stories', label: 'Stories' },
  { key: 'processing', label: 'In progress' },
  { key: 'completed', label: 'Completed' },
  { key: 'failed', label: 'Failed' },
];

@Component({
  selector: 'app-history',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, GenerationCard, Icon],
  template: `
    <div class="mx-auto max-w-7xl">
      <div class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-bold tracking-tight">History</h1>
          <p class="mt-1 text-sm text-muted">{{ total() }} generation{{ total() === 1 ? '' : 's' }}</p>
        </div>
        <div class="relative w-full sm:w-72">
          <input class="input pl-9" type="search" placeholder="Search prompts…" [ngModel]="search()" (ngModelChange)="onSearch($event)" aria-label="Search prompts" />
          <app-icon name="eye" [size]="15" class="absolute left-3 top-2.5 text-muted" />
        </div>
      </div>

      <div class="mt-5 flex flex-wrap gap-1.5">
        @for (f of filters; track f.key) {
          <button type="button" class="chip" [class.chip-active]="filter() === f.key" (click)="setFilter(f.key)">{{ f.label }}</button>
        }
      </div>

      <div class="mt-5 grid grid-cols-2 gap-3 md:grid-cols-3 lg:grid-cols-4 2xl:grid-cols-5">
        @for (g of items(); track g.id) {
          <app-generation-card [generation]="g" (view)="actions.view($event)" (regenerate)="actions.regenerate($event)"
            (variation)="actions.variation($event)" (cancel)="actions.cancel($event)" (remove)="remove($event)"
            (useInProject)="actions.useInProject($event)" />
        } @empty {
          @if (!loading()) {
            <div class="col-span-full rounded-xl border border-dashed border-line p-12 text-center text-sm text-muted">No generations match this filter.</div>
          }
        }
      </div>

      @if (loading()) {
        <p class="mt-6 text-center text-sm text-muted">Loading…</p>
      } @else if (items().length < total()) {
        <div class="mt-6 text-center">
          <button type="button" class="btn btn-secondary" (click)="load(page() + 1)">Load more</button>
        </div>
      }
    </div>
  `,
})
export class HistoryPage implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly store = inject(GenerationStore);
  private readonly toast = inject(ToastService);
  protected readonly actions = inject(GenerationActions);

  protected readonly filters = FILTERS;
  protected readonly filter = signal('');
  protected readonly search = signal('');
  protected readonly items = signal<Generation[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly loading = signal(false);
  private searchTimer?: ReturnType<typeof setTimeout>;
  private unsubscribe?: () => void;

  ngOnInit(): void {
    this.unsubscribe = this.store.onUpdate((g) => {
      if (this.items().some((x) => x.id === g.id)) {
        this.items.update((list) => list.map((x) => (x.id === g.id ? g : x)));
      } else if (this.filter() === '' && !this.search()) {
        this.items.update((list) => [g, ...list]);
        this.total.update((t) => t + 1);
      }
    });
    void this.load(1);
  }

  ngOnDestroy(): void {
    this.unsubscribe?.();
    clearTimeout(this.searchTimer);
  }

  protected setFilter(key: string): void {
    this.filter.set(key);
    void this.load(1);
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => void this.load(1), 300);
  }

  protected async load(page: number): Promise<void> {
    this.loading.set(true);
    try {
      const result = await this.api.generations({ filter: this.filter(), search: this.search(), page, pageSize: 24 });
      result.items.forEach((g) => this.store.upsert(g));
      this.items.update((list) => (page === 1 ? result.items : [...list, ...result.items]));
      this.total.set(result.total);
      this.page.set(page);
    } catch (e) {
      this.toast.error(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  protected async remove(g: Generation): Promise<void> {
    if (await this.actions.remove(g)) {
      this.items.update((list) => list.filter((x) => x.id !== g.id));
      this.total.update((t) => t - 1);
    }
  }
}
