import { Dialog, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, Injectable, Type, inject, input } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Icon } from './icon';

export type DialogSize = 'md' | 'lg';

/**
 * Maximum widths. Passed to the CDK as inline styles because the CDK injects its own overlay
 * styles at runtime (unlayered CSS would override Tailwind's max-w-* utilities).
 */
const maxWidths: Record<DialogSize, string> = {
  md: '32rem',
  lg: '42rem',
};

/**
 * Opens dialogs with the Angular CDK (focus trap, Escape, scroll blocking, a11y).
 * The dialog component receives its data through `DIALOG_DATA` and closes with `DialogRef.close(result)`.
 */
@Injectable({ providedIn: 'root' })
export class DialogService {
  private readonly dialog = inject(Dialog);

  open<TResult = unknown, TData = unknown>(
    component: Type<unknown>,
    data?: TData,
    size: DialogSize = 'md',
  ): Promise<TResult | undefined> {
    const ref = this.dialog.open<TResult, TData>(component, {
      data,
      backdropClass: 'ui-dialog-backdrop',
      panelClass: ['px-4'],
      width: '100%',
      maxWidth: maxWidths[size],
      autoFocus: 'first-tabbable',
      restoreFocus: true,
    });

    return firstValueFrom(ref.closed);
  }
}

/** Layout of every dialog: title, content and actions (`<div dialog-actions>`). */
@Component({
  selector: 'ui-dialog',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block rounded-ui border border-border bg-surface text-foreground shadow-xl' },
  template: `
    <header class="flex items-start justify-between gap-4 px-5 pt-5">
      <div>
        <h2 class="text-lg font-semibold">{{ heading() }}</h2>
        @if (description()) {
          <p class="mt-1 text-sm text-muted">{{ description() }}</p>
        }
      </div>
      <button
        type="button"
        class="rounded-ui p-1 text-muted hover:bg-surface-muted hover:text-foreground cursor-pointer"
        (click)="ref.close()"
        i18n-aria-label="@@ui.close"
        aria-label="Close"
      >
        <ui-icon name="close" />
      </button>
    </header>
    <div class="max-h-[70vh] overflow-y-auto px-5 py-4"><ng-content /></div>
    <footer class="flex flex-wrap justify-end gap-2 border-t border-border px-5 py-3">
      <ng-content select="[dialog-actions]" />
    </footer>
  `,
})
export class DialogLayout {
  protected readonly ref = inject(DialogRef);
  readonly heading = input.required<string>();
  readonly description = input<string | null>(null);
}
