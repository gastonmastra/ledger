import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * Styled table. Project <thead>/<tbody> as usual:
 * `<ui-table><thead><tr><th>…</th></tr></thead><tbody>…</tbody></ui-table>`.
 */
@Component({
  selector: 'ui-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block overflow-x-auto' },
  template: `
    <table
      class="w-full border-collapse text-left text-sm
             [&_th]:border-b [&_th]:border-border [&_th]:px-3 [&_th]:py-2 [&_th]:text-xs [&_th]:font-medium
             [&_th]:uppercase [&_th]:tracking-wide [&_th]:text-muted
             [&_td]:border-b [&_td]:border-border [&_td]:px-3 [&_td]:py-2.5 [&_td]:align-middle
             [&_tbody_tr:last-child_td]:border-b-0 [&_tbody_tr:hover]:bg-surface-muted/50"
    >
      <ng-content />
    </table>
  `,
})
export class Table {}
