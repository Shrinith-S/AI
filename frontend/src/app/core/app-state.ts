import { Injectable, effect, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { SystemStatus } from './models';

export interface Toast {
  id: number;
  kind: 'info' | 'success' | 'error';
  text: string;
}

/** Toast notifications. */
@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private nextId = 1;

  show(text: string, kind: Toast['kind'] = 'info', timeoutMs = 5000): void {
    const toast = { id: this.nextId++, kind, text };
    this.toasts.update((t) => [...t, toast]);
    setTimeout(() => this.dismiss(toast.id), kind === 'error' ? Math.max(timeoutMs, 9000) : timeoutMs);
  }
  success = (text: string) => this.show(text, 'success');
  error = (text: string) => this.show(text, 'error');
  dismiss(id: number): void {
    this.toasts.update((t) => t.filter((x) => x.id !== id));
  }
}

/** Polls /api/system/status so the shell can show ComfyUI availability and Local-Only mode. */
@Injectable({ providedIn: 'root' })
export class StatusService {
  private readonly api = inject(ApiService);
  readonly status = signal<SystemStatus | null>(null);
  readonly apiReachable = signal(true);

  constructor() {
    void this.refresh();
    setInterval(() => void this.refresh(), 20000);
  }

  async refresh(): Promise<void> {
    try {
      this.status.set(await this.api.status());
      this.apiReachable.set(true);
    } catch {
      this.apiReachable.set(false);
    }
  }
}

/** Dark by default; the choice is remembered per browser. */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal<'dark' | 'light'>(this.load());

  constructor() {
    effect(() => {
      const t = this.theme();
      document.documentElement.classList.toggle('dark', t === 'dark');
      try {
        localStorage.setItem('chitra.theme', t);
      } catch {
        // Storage unavailable; theme still applies for this session.
      }
    });
  }

  toggle(): void {
    this.theme.update((t) => (t === 'dark' ? 'light' : 'dark'));
  }

  private load(): 'dark' | 'light' {
    try {
      return localStorage.getItem('chitra.theme') === 'light' ? 'light' : 'dark';
    } catch {
      return 'dark';
    }
  }
}
