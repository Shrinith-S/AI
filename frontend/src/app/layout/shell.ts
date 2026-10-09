import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { StatusService, ThemeService, ToastService } from '../core/app-state';
import { GenerationStore } from '../core/generation-store';
import { RealtimeService } from '../core/realtime.service';
import { GenerationActions } from '../shared/generation-actions';
import { GenerationViewer } from '../shared/generation-viewer';
import { Icon } from '../shared/icon';
import { ProjectPicker } from '../shared/project-picker';

interface NavItem {
  label: string;
  icon: string;
  link: string;
  exact?: boolean;
}

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon, GenerationViewer, ProjectPicker],
  template: `
    <div class="flex h-full">
      <!-- Sidebar -->
      <aside class="fixed inset-y-0 left-0 z-40 flex w-64 flex-col border-r border-line bg-panel transition-transform lg:static lg:translate-x-0"
        [class.-translate-x-full]="!menuOpen()">
        <a routerLink="/" class="flex items-center gap-2.5 px-5 py-5" (click)="menuOpen.set(false)">
          <span class="grid h-9 w-9 place-items-center rounded-xl bg-gradient-to-br from-amber-400 to-rose-500 text-lg font-bold text-white shadow-lg">C</span>
          <span>
            <span class="block text-base font-semibold leading-tight">Chitra Studio</span>
            <span class="block text-[11px] text-muted">Self-hosted AI creative studio</span>
          </span>
        </a>
        <nav class="flex-1 space-y-0.5 overflow-y-auto px-3 pb-4">
          @for (section of nav; track section.title) {
            <p class="px-3 pb-1 pt-4 text-[10px] font-semibold uppercase tracking-wider text-muted/80">{{ section.title }}</p>
            @for (item of section.items; track item.link) {
              <a [routerLink]="item.link" routerLinkActive="!bg-panel-2 !text-ink" [routerLinkActiveOptions]="{ exact: item.exact ?? false }"
                class="flex items-center gap-3 rounded-lg px-3 py-2 text-sm text-muted transition hover:bg-panel-2 hover:text-ink"
                (click)="menuOpen.set(false)">
                <app-icon [name]="item.icon" [size]="17" /> {{ item.label }}
              </a>
            }
          }
        </nav>
        <div class="border-t border-line p-4 text-xs">
          @if (status.status(); as s) {
            <div class="flex items-center gap-2">
              <span class="h-2 w-2 rounded-full" [class]="s.aiProvider.isAvailable ? 'bg-emerald-500' : 'bg-red-500'"></span>
              <span class="truncate text-muted" [title]="s.aiProvider.message">{{ s.aiProvider.name }} {{ s.aiProvider.isAvailable ? 'connected' : 'offline' }}</span>
            </div>
            @if (s.queuedGenerations + s.processingGenerations > 0) {
              <p class="mt-1.5 text-muted">{{ s.processingGenerations }} running · {{ s.queuedGenerations }} queued</p>
            }
          } @else if (!status.apiReachable()) {
            <div class="flex items-center gap-2 text-red-400"><span class="h-2 w-2 rounded-full bg-red-500"></span> API unreachable</div>
          }
        </div>
      </aside>
      @if (menuOpen()) {
        <div class="fixed inset-0 z-30 bg-black/50 lg:hidden" (click)="menuOpen.set(false)"></div>
      }

      <!-- Main -->
      <div class="flex min-w-0 flex-1 flex-col">
        <header class="sticky top-0 z-20 flex items-center gap-3 border-b border-line bg-bg/80 px-4 py-3 backdrop-blur lg:px-8">
          <button type="button" class="btn-icon lg:hidden" (click)="menuOpen.set(true)" aria-label="Open menu"><app-icon name="menu" /></button>
          <div class="flex-1"></div>
          @if (status.status(); as s) {
            @if (s.aiProvider.name === 'Kaggle') {
              <a routerLink="/settings" class="hidden items-center gap-1.5 rounded-full border border-sky-500/30 bg-sky-500/10 px-3 py-1 text-xs font-medium text-sky-400 sm:inline-flex"
                title="Generation runs on Kaggle's GPUs. Prompts and input images are uploaded to Kaggle; free within your weekly GPU quota.">
                <app-icon name="cpu" [size]="13" /> Kaggle GPU — remote, free quota
              </a>
            } @else if (s.localOnly) {
              <a routerLink="/settings" class="hidden items-center gap-1.5 rounded-full border border-emerald-500/30 bg-emerald-500/10 px-3 py-1 text-xs font-medium text-emerald-500 sm:inline-flex"
                [title]="s.costNotice">
                <app-icon name="zap" [size]="13" /> Local AI Mode — No API charges
              </a>
            } @else {
              <a routerLink="/settings" class="hidden items-center gap-1.5 rounded-full border border-amber-500/30 bg-amber-500/10 px-3 py-1 text-xs font-medium text-amber-500 sm:inline-flex">
                <app-icon name="alert" [size]="13" /> Local-Only is off
              </a>
            }
          }
          @if (!realtime.connected()) {
            <span class="hidden text-xs text-muted md:inline" title="Live updates reconnecting; progress is polled meanwhile">○ live updates reconnecting</span>
          }
          <button type="button" class="btn-icon" (click)="theme.toggle()" [attr.aria-label]="'Switch to ' + (theme.theme() === 'dark' ? 'light' : 'dark') + ' mode'">
            <app-icon [name]="theme.theme() === 'dark' ? 'sun' : 'moon'" />
          </button>
        </header>

        @if (status.status(); as s) {
          @if (s.aiProvider.isAvailable && s.aiProvider.name === 'Kaggle' && showBanner()) {
            <div class="flex items-start gap-3 border-b border-sky-500/20 bg-sky-500/10 px-4 py-2 text-xs text-sky-300 lg:px-8">
              <app-icon name="info" [size]="15" class="mt-0.5" />
              <p class="flex-1">
                Kaggle batch mode: jobs wait a few seconds to be batched, then each run spends several minutes on setup before generating.
                There is no step-by-step progress, and a run cannot be stopped once it starts.
              </p>
            </div>
          }
          @if (!s.aiProvider.isAvailable && showBanner()) {
            <div class="flex items-start gap-3 border-b border-red-500/20 bg-red-500/10 px-4 py-2.5 text-sm text-red-400 lg:px-8">
              <app-icon name="alert" [size]="16" class="mt-0.5" />
              <p class="flex-1">{{ s.aiProvider.message }}</p>
              <button type="button" class="text-xs underline" (click)="status.refresh()">Retry</button>
            </div>
          }
        }

        <main class="flex-1 overflow-y-auto px-4 py-6 lg:px-8">
          <router-outlet />
        </main>
      </div>
    </div>

    @if (actions.viewing(); as viewing) {
      <app-generation-viewer [generation]="store.get(viewing.id) ?? viewing"
        (close)="actions.viewing.set(null)"
        (regenerate)="actions.regenerate($event)"
        (variation)="actions.variation($event)"
        (remove)="actions.remove($event)"
        (animate)="actions.animate($event)"
        (useInProject)="actions.useInProject($event)" />
    }
    @if (actions.pickProjectFor(); as g) {
      <app-project-picker (picked)="actions.attach($event, g)" (close)="actions.pickProjectFor.set(null)" />
    }

    <div class="pointer-events-none fixed bottom-4 right-4 z-[60] flex w-[min(24rem,calc(100vw-2rem))] flex-col gap-2">
      @for (t of toast.toasts(); track t.id) {
        <div class="pointer-events-auto flex items-start gap-2 rounded-lg border px-3.5 py-2.5 text-sm shadow-xl backdrop-blur"
          [class]="t.kind === 'error' ? 'border-red-500/40 bg-red-950/90 text-red-100' : t.kind === 'success' ? 'border-emerald-500/40 bg-emerald-950/90 text-emerald-100' : 'border-line bg-panel/95 text-ink'"
          role="status">
          <p class="flex-1">{{ t.text }}</p>
          <button type="button" class="opacity-70 hover:opacity-100" (click)="toast.dismiss(t.id)" aria-label="Dismiss"><app-icon name="x" [size]="14" /></button>
        </div>
      }
    </div>
  `,
})
export class Shell {
  protected readonly status = inject(StatusService);
  protected readonly theme = inject(ThemeService);
  protected readonly toast = inject(ToastService);
  protected readonly realtime = inject(RealtimeService);
  protected readonly actions = inject(GenerationActions);
  protected readonly store = inject(GenerationStore);
  protected readonly menuOpen = signal(false);
  private readonly url = signal('/');

  /** The offline banner is shown on creation pages, where it matters. */
  protected readonly showBanner = computed(() => this.url().startsWith('/create') || this.url().startsWith('/projects/') || this.url() === '/');

  protected readonly nav: { title: string; items: NavItem[] }[] = [
    {
      title: 'Studio',
      items: [
        { label: 'Dashboard', icon: 'home', link: '/', exact: true },
        { label: 'Create', icon: 'sparkles', link: '/create', exact: true },
      ],
    },
    {
      title: 'Generate',
      items: [
        { label: 'Image Generator', icon: 'image', link: '/create/image' },
        { label: 'Video Generator', icon: 'video', link: '/create/video' },
        { label: 'Image to Video', icon: 'film', link: '/create/image-to-video' },
        { label: 'Story Video', icon: 'book', link: '/create/story' },
      ],
    },
    {
      title: 'Library',
      items: [
        { label: 'Characters', icon: 'user', link: '/characters' },
        { label: 'Styles', icon: 'palette', link: '/styles' },
        { label: 'Projects', icon: 'folder', link: '/projects' },
        { label: 'History', icon: 'clock', link: '/history' },
      ],
    },
    {
      title: 'System',
      items: [
        { label: 'Models', icon: 'box', link: '/models' },
        { label: 'Settings', icon: 'sliders', link: '/settings' },
      ],
    },
  ];

  constructor() {
    inject(RealtimeService).start();
    const router = inject(Router);
    router.events.pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd)).subscribe((e) => this.url.set(e.urlAfterRedirects));
  }
}
