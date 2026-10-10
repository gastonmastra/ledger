import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Label + control + hint/error. The projected control is associated through the wrapping <label>. */
@Component({
  selector: 'ui-form-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <label class="flex flex-col gap-1.5">
      <span class="text-sm font-medium text-foreground">{{ label() }}</span>
      <ng-content />
    </label>
    @if (error()) {
      <p class="mt-1 text-xs text-danger" role="alert">{{ error() }}</p>
    } @else if (hint()) {
      <p class="mt-1 text-xs text-muted">{{ hint() }}</p>
    }
  `,
})
export class FormField {
  readonly label = input.required<string>();
  readonly hint = input<string | null>(null);
  readonly error = input<string | null>(null);
}
