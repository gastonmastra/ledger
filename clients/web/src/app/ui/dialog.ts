import { Dialog, DialogConfig, DialogRef } from '@angular/cdk/dialog';
import { Overlay } from '@angular/cdk/overlay';
import { ChangeDetectionStrategy, Component, Injectable, Type, inject, input } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Icon } from './icon';

/** md and lg: centered. panel: a side panel on the right (desktop) or a bottom sheet (phones). */
export type DialogSize = 'md' | 'lg' | 'panel';

/**
 * Maximum widths. Passed to the CDK as inline styles because the CDK injects its own overlay
 * styles at runtime (unlayered CSS would override Tailwind's max-w-* utilities).
 */
const maxWidths: Record<Exclude<DialogSize, 'panel'>, string> = {
  md: '32rem',
  lg: '42rem',
};

/** Below this width the panel is a bottom sheet (Tailwind's sm breakpoint). */
const wideQuery = '(min-width: 640px)';

/**
 * Opens dialogs with the Angular CDK (focus trap, Escape, scroll blocking, a11y).
 * The dialog component receives its data through `DIALOG_DATA` and closes with `DialogRef.close(result)`.
 */
@Injectable({ providedIn: 'root' })
export class DialogService {
  private readonly dialog = inject(Dialog);
  private readonly overlay = inject(Overlay);

  open<TResult = unknown, TData = unknown>(
    component: Type<unknown>,
    data?: TData,
    size: DialogSize = 'md',
  ): Promise<TResult | undefined> {
    const common: DialogConfig<TData, DialogRef<TResult, unknown>> = {
      data,
      backdropClass: 'ui-dialog-backdrop',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
    };

    const ref = this.dialog.open<TResult, TData>(
      component,
      size === 'panel' ? { ...common, ...this.panelConfig() } : {
        ...common,
        panelClass: ['px-4'],
        width: '100%',
        maxWidth: maxWidths[size],
      },
    );

    return firstValueFrom(ref.closed);
  }

  private panelConfig(): Pick<DialogConfig, 'panelClass' | 'positionStrategy' | 'width' | 'maxWidth' | 'height' | 'maxHeight'> {
    const wide = typeof matchMedia === 'function' && matchMedia(wideQuery).matches;
    const position = this.overlay.position().global();

    return wide
      ? {
          panelClass: ['ui-panel', 'ui-panel-side'],
          positionStrategy: position.right('0').top('0'),
          width: '24rem',
          maxWidth: '100vw',
          height: '100%',
        }
      : {
          panelClass: ['ui-panel', 'ui-panel-sheet'],
          positionStrategy: position.bottom('0').centerHorizontally(),
          width: '100%',
          maxWidth: '100vw',
          maxHeight: '90vh',
        };
  }
}

/** Layout of every dialog: title, content and actions (`<div dialog-actions>`). */
@Component({
  selector: 'ui-dialog',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'ui-dialog block rounded-ui border border-border bg-surface text-foreground shadow-xl' },
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
    <div class="ui-dialog-body max-h-[70vh] overflow-y-auto px-5 py-4"><ng-content /></div>
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
