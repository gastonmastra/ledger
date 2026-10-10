import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

@Component({
  selector: 'ui-spinner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'status', class: 'inline-flex' },
  template: `
    <svg [class]="classes()" viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="3" class="opacity-25" />
      <path d="M22 12a10 10 0 0 0-10-10" stroke="currentColor" stroke-width="3" stroke-linecap="round" />
    </svg>
    <span class="sr-only" i18n="@@ui.loading">Loading…</span>
  `,
})
export class Spinner {
  readonly size = input<'sm' | 'md'>('md');
  protected readonly classes = computed(() => `animate-spin ${this.size() === 'sm' ? 'size-4' : 'size-6'}`);
}
