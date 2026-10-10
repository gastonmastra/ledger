import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'ui-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'mb-6 flex flex-wrap items-end justify-between gap-4' },
  template: `
    <div>
      <h1 class="text-2xl font-semibold tracking-tight text-foreground">{{ heading() }}</h1>
      @if (subtitle()) {
        <p class="mt-1 text-sm text-muted">{{ subtitle() }}</p>
      }
    </div>
    <div class="flex flex-wrap items-center gap-2"><ng-content /></div>
  `,
})
export class PageHeader {
  readonly heading = input.required<string>();
  readonly subtitle = input<string | null>(null);
}
