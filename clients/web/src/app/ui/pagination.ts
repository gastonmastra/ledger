import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Button } from './button';

@Component({
  selector: 'ui-pagination',
  imports: [Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex items-center justify-between gap-3 text-sm text-muted' },
  template: `
    <span i18n="@@ui.pageOf">Page {{ page() }} of {{ totalPages() || 1 }} · {{ totalCount() }} items</span>
    <div class="flex gap-2">
      <button uiButton variant="secondary" size="sm" type="button" [disabled]="page() <= 1" (click)="pageChange.emit(page() - 1)" i18n="@@ui.previous">
        Previous
      </button>
      <button uiButton variant="secondary" size="sm" type="button" [disabled]="page() >= totalPages()" (click)="pageChange.emit(page() + 1)" i18n="@@ui.next">
        Next
      </button>
    </div>
  `,
})
export class Pagination {
  readonly page = input.required<number>();
  readonly totalPages = input.required<number>();
  readonly totalCount = input.required<number>();
  readonly pageChange = output<number>();
}
