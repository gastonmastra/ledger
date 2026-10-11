import { Directive, ElementRef, inject, output } from '@angular/core';

/** Elements inside a row that keep their own click (they don't open the row's detail). */
const interactive = 'button, a, input, select, textarea, label, ui-actions-menu, [cdkMenu]';

/**
 * Makes a table row or list item open its detail: click, Enter or Space. Clicks on buttons, links or
 * inputs inside it keep doing their own thing. The element keeps its own role (row, list item), so tables
 * and lists stay tables and lists for screen readers; it is focusable and announces that it opens a dialog.
 * Usage: `<tr uiRowAction (rowOpen)="open(item)">`.
 */
@Directive({
  selector: '[uiRowAction]',
  host: {
    tabindex: '0',
    'aria-haspopup': 'dialog',
    class: 'cursor-pointer transition-colors hover:bg-surface-muted focus-visible:bg-surface-muted',
    '(click)': 'onClick($event)',
    '(keydown.enter)': 'onKey($event)',
    '(keydown.space)': 'onKey($event)',
  },
})
export class RowAction {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly rowOpen = output<void>();

  protected onClick(event: MouseEvent): void {
    const target = event.target as HTMLElement | null;
    const inner = target?.closest(interactive);

    if (inner && inner !== this.host.nativeElement && this.host.nativeElement.contains(inner)) return;
    this.rowOpen.emit();
  }

  protected onKey(event: Event): void {
    if (event.target !== this.host.nativeElement) return;
    event.preventDefault();
    this.rowOpen.emit();
  }
}
