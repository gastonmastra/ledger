import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Icon } from './icon';

/** An active filter, e.g. { key: 'walletId', label: 'Wallet: Revolut' }. */
export interface FilterChip {
  key: string;
  label: string;
}

/** The active filters as removable chips, plus "Clear all". Renders nothing when there are none. */
@Component({
  selector: 'ui-filter-chips',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (chips().length) {
      <div class="flex flex-wrap items-center gap-2">
        @for (chip of chips(); track chip.key) {
          <span class="inline-flex items-center gap-1 rounded-full bg-primary-soft py-1 pl-3 pr-1 text-xs text-primary-soft-foreground">
            {{ chip.label }}
            <button type="button" class="cursor-pointer rounded-full p-0.5 hover:bg-surface" (click)="remove.emit(chip.key)"
                    i18n-aria-label="@@ui.removeFilter" aria-label="Remove filter">
              <ui-icon name="close" size="sm" />
            </button>
          </span>
        }
        <button type="button" class="cursor-pointer text-xs text-muted underline-offset-2 hover:underline" (click)="clear.emit()"
                i18n="@@ui.clearAll">Clear all</button>
      </div>
    }
  `,
})
export class FilterChips {
  readonly chips = input.required<FilterChip[]>();
  readonly remove = output<string>();
  readonly clear = output<void>();
}
