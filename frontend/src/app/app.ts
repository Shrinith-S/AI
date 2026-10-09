import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ThemeService } from './core/app-state';
import { Shell } from './layout/shell';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Shell],
  template: `<app-shell />`,
  host: { class: 'block h-full' },
})
export class App {
  // Instantiated eagerly so the saved theme applies before first paint of pages.
  private readonly theme = inject(ThemeService);
}
