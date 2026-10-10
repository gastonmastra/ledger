import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ToastService, ToastTone } from '../core/notifications/toast.service';
import { Icon } from './icon';

const tones: Record<ToastTone, string> = {
  info: 'border-border',
  success: 'border-success',
  error: 'border-danger',
};

/** Renders the toasts of ToastService. Placed once in the app shell. */
@Component({
  selector: 'ui-toast-container',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'pointer-events-none fixed inset-x-0 bottom-20 z-[1100] flex flex-col items-center gap-2 px-4 lg:bottom-6 lg:items-end',
    'aria-live': 'polite',
  },
  template: `
    @for (toast of toasts.toasts(); track toast.id) {
      <div
        class="pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-ui border-l-4 bg-surface p-3 text-sm text-foreground shadow-lg ring-1 ring-border"
        [class]="toneClass(toast.tone)"
        [attr.role]="toast.tone === 'error' ? 'alert' : 'status'"
      >
        <p class="flex-1">{{ toast.message }}</p>
        <button
          type="button"
          class="text-muted hover:text-foreground cursor-pointer"
          (click)="toasts.dismiss(toast.id)"
          i18n-aria-label="@@ui.dismiss"
          aria-label="Dismiss"
        >
          <ui-icon name="close" size="sm" />
        </button>
      </div>
    }
  `,
})
export class ToastContainer {
  protected readonly toasts = inject(ToastService);

  protected toneClass(tone: ToastTone): string {
    return tones[tone];
  }
}
