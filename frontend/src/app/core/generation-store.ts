import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { Generation, isTerminal } from './models';
import { RealtimeService } from './realtime.service';

/**
 * Single source of truth for generations shown on screen. SignalR pushes updates; when the hub is disconnected,
 * active generations are polled so progress never silently freezes.
 */
@Injectable({ providedIn: 'root' })
export class GenerationStore {
  private readonly api = inject(ApiService);
  private readonly realtime = inject(RealtimeService);
  private readonly items = signal<Record<string, Generation>>({});
  private readonly listeners = new Set<(g: Generation) => void>();

  /** Increments on every update; lists can react to it to refresh. */
  readonly version = signal(0);

  constructor() {
    this.realtime.generationUpdated.subscribe((g) => this.upsert(g));
    setInterval(() => void this.pollActive(), 3000);
  }

  upsert(g: Generation): void {
    this.items.update((all) => ({ ...all, [g.id]: g }));
    this.version.update((v) => v + 1);
    this.listeners.forEach((l) => l(g));
  }

  remove(id: string): void {
    this.items.update((all) => {
      const { [id]: _, ...rest } = all;
      return rest;
    });
    this.version.update((v) => v + 1);
  }

  watch(id: Signal<string | null>): Signal<Generation | null> {
    return computed(() => {
      const key = id();
      return key ? (this.items()[key] ?? null) : null;
    });
  }

  get(id: string): Generation | null {
    return this.items()[id] ?? null;
  }

  /** Called for every update, e.g. so a page can react when its generation completes. */
  onUpdate(listener: (g: Generation) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  private async pollActive(): Promise<void> {
    if (this.realtime.connected()) return;
    const active = Object.values(this.items()).filter((g) => !isTerminal(g.status));
    for (const g of active) {
      try {
        this.upsert(await this.api.generation(g.id));
      } catch {
        // Backend unreachable; try again next tick.
      }
    }
  }
}
