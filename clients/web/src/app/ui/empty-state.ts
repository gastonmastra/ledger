import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'ui-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col items-center justify-center gap-3 py-10 text-center' },
  template: `
    <p class="text-sm text-muted">{{ message() }}</p>
    <ng-content />
  `,
})
export class EmptyState {
  readonly message = input.required<string>();
}
