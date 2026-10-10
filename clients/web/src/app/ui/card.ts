import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Surface with an optional header: `<ui-card heading="Wallets"><button card-actions>…</button>…</ui-card>`. */
@Component({
  selector: 'ui-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block rounded-ui border border-border bg-surface shadow-sm' },
  template: `
    @if (heading()) {
      <header class="flex items-center justify-between gap-3 border-b border-border px-4 py-3">
        <h2 class="text-sm font-semibold text-foreground">{{ heading() }}</h2>
        <div class="flex items-center gap-2"><ng-content select="[card-actions]" /></div>
      </header>
    }
    <div [class]="padded() ? 'p-4' : ''"><ng-content /></div>
  `,
})
export class Card {
  readonly heading = input<string | null>(null);
  readonly padded = input(true);
}
