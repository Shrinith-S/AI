import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon } from './icon';

export interface CreateCard {
  title: string;
  description: string;
  icon: string;
  link: string;
  gradient: string;
}

export const CREATE_CARDS: CreateCard[] = [
  { title: 'Text to Image', description: 'Turn a prompt into images with SDXL, FLUX or SD 1.5.', icon: 'image', link: '/create/image', gradient: 'from-amber-500/25 to-orange-600/5' },
  { title: 'Text to Video', description: 'Generate short clips with LTX-Video, Wan or HunyuanVideo.', icon: 'video', link: '/create/video', gradient: 'from-rose-500/25 to-pink-600/5' },
  { title: 'Image to Video', description: 'Bring a still image to life with motion and camera moves.', icon: 'film', link: '/create/image-to-video', gradient: 'from-violet-500/25 to-indigo-600/5' },
  { title: 'Story Video', description: 'Plan a story into scenes, generate each shot, export a film.', icon: 'book', link: '/create/story', gradient: 'from-sky-500/25 to-cyan-600/5' },
  { title: 'Character Creator', description: 'Define reusable characters to keep them consistent.', icon: 'user', link: '/characters', gradient: 'from-emerald-500/25 to-teal-600/5' },
  { title: 'Style Creator', description: 'Save looks you like and apply them to any prompt.', icon: 'palette', link: '/styles', gradient: 'from-fuchsia-500/25 to-purple-600/5' },
];

@Component({
  selector: 'app-create-cards',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Icon],
  template: `
    <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3" [class.2xl:grid-cols-6]="compact()">
      @for (card of cards; track card.link) {
        <a [routerLink]="card.link" class="card group relative overflow-hidden p-4 transition hover:-translate-y-0.5 hover:border-amber-500/50">
          <div class="pointer-events-none absolute inset-0 bg-gradient-to-br opacity-80" [class]="card.gradient"></div>
          <div class="relative">
            <span class="mb-3 grid h-10 w-10 place-items-center rounded-lg bg-panel-2 text-amber-500 ring-1 ring-line">
              <app-icon [name]="card.icon" [size]="20" />
            </span>
            <h3 class="font-semibold">{{ card.title }}</h3>
            <p class="mt-1 text-sm text-muted">{{ card.description }}</p>
            <span class="mt-3 inline-flex items-center gap-1 text-xs font-medium text-amber-500 opacity-0 transition group-hover:opacity-100">
              Start <app-icon name="right" [size]="13" />
            </span>
          </div>
        </a>
      }
    </div>
  `,
})
export class CreateCards {
  readonly compact = input(false);
  protected readonly cards = CREATE_CARDS;
}
