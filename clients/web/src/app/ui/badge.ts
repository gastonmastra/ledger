import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type BadgeTone = 'neutral' | 'primary' | 'success' | 'danger' | 'warning';

const tones: Record<BadgeTone, string> = {
  neutral: 'bg-surface-muted text-muted',
  primary: 'bg-primary-soft text-primary-soft-foreground',
  success: 'bg-success-soft text-success',
  danger: 'bg-danger-soft text-danger',
  warning: 'bg-warning-soft text-warning',
};

@Component({
  selector: 'ui-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class]': 'classes()' },
  template: `<ng-content />`,
})
export class Badge {
  readonly tone = input<BadgeTone>('neutral');
  protected readonly classes = computed(
    () => `inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${tones[this.tone()]}`,
  );
}
