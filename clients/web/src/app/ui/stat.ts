import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type StatTone = 'default' | 'positive' | 'negative' | 'muted';

const tones: Record<StatTone, string> = {
  default: 'text-foreground',
  positive: 'text-success',
  negative: 'text-danger',
  muted: 'text-muted',
};

/** A labelled figure: "Net 1,482.00 €". */
@Component({
  selector: 'ui-stat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-1' },
  template: `
    <span class="text-xs font-medium uppercase tracking-wide text-muted">{{ label() }}</span>
    <span [class]="valueClasses()">{{ value() }}</span>
  `,
})
export class Stat {
  readonly label = input.required<string>();
  readonly value = input.required<string>();
  readonly tone = input<StatTone>('default');
  readonly emphasis = input(false);

  protected readonly valueClasses = computed(
    () => `${this.emphasis() ? 'text-2xl font-semibold' : 'text-lg font-medium'} tabular-nums ${tones[this.tone()]}`,
  );
}
