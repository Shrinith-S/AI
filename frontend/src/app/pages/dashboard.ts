import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { StatusService } from '../core/app-state';
import { GenerationStore } from '../core/generation-store';
import { Generation } from '../core/models';
import { CreateCards } from '../shared/create-cards';
import { GenerationActions } from '../shared/generation-actions';
import { GenerationCard } from '../shared/generation-card';
import { Icon } from '../shared/icon';

@Component({
  selector: 'app-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CreateCards, GenerationCard, Icon, RouterLink],
  template: `
    <div class="mx-auto max-w-7xl space-y-10">
      <section>
        <h1 class="text-2xl font-bold tracking-tight sm:text-3xl">Create with AI</h1>
        <p class="mt-1 text-muted">Images, videos and stories generated on your own hardware with open-source models.</p>
        <div class="mt-6"><app-create-cards /></div>
      </section>

      @if (status.status(); as s) {
        <section class="grid gap-3 md:grid-cols-3">
          <div class="card flex items-start gap-3 p-4">
            <span class="mt-1 h-2.5 w-2.5 shrink-0 rounded-full" [class]="s.aiProvider.isAvailable ? 'bg-emerald-500' : 'bg-red-500'"></span>
            <div class="min-w-0">
              <p class="text-sm font-medium">{{ s.aiProvider.name }} {{ s.aiProvider.isAvailable ? 'ready' : 'offline' }}</p>
              <p class="mt-0.5 text-xs text-muted">{{ s.aiProvider.message }}</p>
            </div>
          </div>
          <div class="card flex items-start gap-3 p-4">
            <app-icon name="cpu" class="mt-0.5 text-muted" />
            <div class="min-w-0">
              <p class="text-sm font-medium">Hardware: <span [class]="ratingClass(s.hardwareRating)">{{ s.hardwareRating }}</span></p>
              <p class="mt-0.5 text-xs text-muted">{{ s.hardwareRatingReason }}</p>
            </div>
          </div>
          <a routerLink="/settings" class="card flex items-start gap-3 p-4 hover:border-emerald-500/40">
            <app-icon name="zap" class="mt-0.5 text-emerald-500" />
            <div class="min-w-0">
              <p class="text-sm font-medium">{{ s.localOnly ? 'Local-Only mode is on' : 'Local-Only mode is off' }}</p>
              <p class="mt-0.5 text-xs text-muted">No API charges. Your real cost is your hardware and the electricity it uses.</p>
            </div>
          </a>
        </section>
      }

      <section>
        <div class="mb-4 flex items-end justify-between">
          <h2 class="text-lg font-semibold">Recent generations</h2>
          <a routerLink="/history" class="text-sm text-amber-500 hover:underline">View all</a>
        </div>
        @if (loading()) {
          <p class="text-sm text-muted">Loading…</p>
        } @else {
          <div class="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
            @for (g of recent(); track g.id) {
              <app-generation-card [generation]="g" [showActions]="false" (view)="actions.view($event)" />
            } @empty {
              <div class="col-span-full rounded-xl border border-dashed border-line p-10 text-center text-sm text-muted">
                Nothing generated yet. Pick a card above to start.
              </div>
            }
          </div>
        }
      </section>
    </div>
  `,
})
export class DashboardPage implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly store = inject(GenerationStore);
  protected readonly status = inject(StatusService);
  protected readonly actions = inject(GenerationActions);
  protected readonly recent = signal<Generation[]>([]);
  protected readonly loading = signal(true);
  private unsubscribe?: () => void;

  async ngOnInit(): Promise<void> {
    this.unsubscribe = this.store.onUpdate((g) =>
      this.recent.update((list) => (list.some((x) => x.id === g.id) ? list.map((x) => (x.id === g.id ? g : x)) : [g, ...list].slice(0, 12))),
    );
    try {
      const page = await this.api.generations({ pageSize: 12 });
      page.items.forEach((g) => this.store.upsert(g));
      this.recent.set(page.items);
    } catch {
      // The shell already reports an unreachable API.
    } finally {
      this.loading.set(false);
    }
  }

  ngOnDestroy(): void {
    this.unsubscribe?.();
  }

  protected ratingClass(rating: string): string {
    return rating === 'Recommended' ? 'text-emerald-500' : rating === 'Limited' ? 'text-amber-500' : 'text-red-400';
  }
}
