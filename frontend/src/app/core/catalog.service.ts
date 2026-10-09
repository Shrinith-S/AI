import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { AiModel, Character, Style } from './models';

/** Caches the lists every generator needs: models, styles and characters. */
@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly api = inject(ApiService);

  readonly models = signal<AiModel[]>([]);
  readonly styles = signal<Style[]>([]);
  readonly characters = signal<Character[]>([]);
  readonly loaded = signal(false);
  private loading?: Promise<void>;

  ensureLoaded(): Promise<void> {
    return (this.loading ??= this.reload());
  }

  async reload(): Promise<void> {
    const [models, styles, characters] = await Promise.allSettled([this.api.models(), this.api.styles(), this.api.characters()]);
    if (models.status === 'fulfilled') this.models.set(models.value);
    if (styles.status === 'fulfilled') this.styles.set(styles.value);
    if (characters.status === 'fulfilled') this.characters.set(characters.value);
    this.loaded.set(true);
  }

  async reloadModels(): Promise<void> {
    this.models.set(await this.api.models());
  }
}
