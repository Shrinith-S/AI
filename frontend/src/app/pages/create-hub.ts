import { ChangeDetectionStrategy, Component } from '@angular/core';
import { CreateCards } from '../shared/create-cards';

@Component({
  selector: 'app-create-hub',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CreateCards],
  template: `
    <div class="mx-auto max-w-6xl">
      <h1 class="text-2xl font-bold tracking-tight">Create</h1>
      <p class="mt-1 text-muted">Choose what to make. Everything runs through your local ComfyUI server.</p>
      <div class="mt-6"><app-create-cards /></div>
      <div class="mt-8 grid gap-3 text-sm text-muted md:grid-cols-3">
        <div class="card p-4"><p class="font-medium text-ink">1. Prompt</p><p class="mt-1">Describe the subject, setting, lighting and mood. The enhancer can expand short prompts for you.</p></div>
        <div class="card p-4"><p class="font-medium text-ink">2. Generate</p><p class="mt-1">Jobs run in the background queue; progress streams live and you can keep working.</p></div>
        <div class="card p-4"><p class="font-medium text-ink">3. Reuse</p><p class="mt-1">Regenerate, vary the seed, animate an image or add results to a project.</p></div>
      </div>
    </div>
  `,
})
export class CreateHubPage {}
