import { ChangeDetectionStrategy, Component, booleanAttribute, computed, input } from '@angular/core';
import { cx } from './cx';
import { Spinner } from './spinner';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonSize = 'sm' | 'md';

const base =
  'inline-flex items-center justify-center gap-2 rounded-ui font-medium transition-colors ' +
  'disabled:pointer-events-none disabled:opacity-50 cursor-pointer whitespace-nowrap';

const variants: Record<ButtonVariant, string> = {
  primary: 'bg-primary text-primary-foreground hover:bg-primary-hover shadow-sm',
  secondary: 'border border-border bg-surface text-foreground hover:bg-surface-muted',
  ghost: 'text-foreground hover:bg-surface-muted',
  danger: 'bg-danger text-primary-foreground hover:opacity-90 shadow-sm',
};

const sizes: Record<ButtonSize, string> = {
  sm: 'h-8 px-3 text-sm',
  md: 'h-10 px-4 text-sm',
};

/** `<button uiButton variant="secondary">` or `<a uiButton routerLink="...">`. */
@Component({
  selector: 'button[uiButton], a[uiButton]',
  imports: [Spinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[class]': 'classes()',
    '[attr.disabled]': 'disabled() || loading() ? true : null',
    '[attr.aria-busy]': 'loading() || null',
  },
  template: `
    @if (loading()) {
      <ui-spinner size="sm" />
    }
    <ng-content />
  `,
})
export class Button {
  readonly variant = input<ButtonVariant>('primary');
  readonly size = input<ButtonSize>('md');
  readonly loading = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });

  protected readonly classes = computed(() => cx(base, variants[this.variant()], sizes[this.size()]));
}
