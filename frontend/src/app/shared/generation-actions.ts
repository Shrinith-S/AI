import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService } from '../core/api.service';
import { ToastService } from '../core/app-state';
import { errorMessage } from '../core/errors';
import { GenerationStore } from '../core/generation-store';
import { Generation } from '../core/models';

/** Actions shared by every place that lists generations (dashboard, generators, history, projects). */
@Injectable({ providedIn: 'root' })
export class GenerationActions {
  private readonly api = inject(ApiService);
  private readonly store = inject(GenerationStore);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  /** Generation waiting for the user to choose a project (drives the project picker dialog). */
  readonly pickProjectFor = signal<Generation | null>(null);
  /** Generation shown in the details viewer. */
  readonly viewing = signal<Generation | null>(null);

  view(g: Generation): void {
    this.viewing.set(g);
  }

  async regenerate(g: Generation): Promise<Generation | null> {
    return this.run(() => this.api.regenerate(g.id), 'Regenerating with the same settings');
  }

  async variation(g: Generation): Promise<Generation | null> {
    return this.run(() => this.api.variation(g.id), 'Creating a variation with a new seed');
  }

  async cancel(g: Generation): Promise<void> {
    try {
      this.store.upsert(await this.api.cancelGeneration(g.id));
      this.toast.show('Generation cancelled');
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  async remove(g: Generation): Promise<boolean> {
    if (!confirm('Delete this generation and its files?')) return false;
    try {
      await this.api.deleteGeneration(g.id);
      this.store.remove(g.id);
      if (this.viewing()?.id === g.id) this.viewing.set(null);
      this.toast.show('Deleted');
      return true;
    } catch (e) {
      this.toast.error(errorMessage(e));
      return false;
    }
  }

  animate(assetId: string): void {
    this.viewing.set(null);
    void this.router.navigate(['/create/image-to-video'], { queryParams: { asset: assetId } });
  }

  useInProject(g: Generation): void {
    this.pickProjectFor.set(g);
  }

  async attach(projectId: string, g: Generation): Promise<void> {
    try {
      await this.api.attachToProject(projectId, { generationId: g.id });
      this.toast.success('Added to project');
      this.pickProjectFor.set(null);
    } catch (e) {
      this.toast.error(errorMessage(e));
    }
  }

  private async run(action: () => Promise<Generation>, message: string): Promise<Generation | null> {
    try {
      const created = await action();
      this.store.upsert(created);
      this.toast.show(message);
      this.viewing.set(null);
      return created;
    } catch (e) {
      this.toast.error(errorMessage(e));
      return null;
    }
  }
}
