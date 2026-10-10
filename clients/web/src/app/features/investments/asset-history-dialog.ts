import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { LedgerApi } from '../../core/api/ledger-api';
import { Asset } from '../../core/api/models';
import { priceSideLabel } from '../../core/i18n/labels';
import { Badge, Button, DialogLayout, EmptyState, Spinner, Table } from '../../ui';
import { PricePipe } from './formatting';
import { TradesTable } from './trades-table';

/** Trades and known prices of one asset. */
@Component({
  selector: 'app-asset-history-dialog',
  imports: [DatePipe, DialogLayout, Button, Table, Badge, EmptyState, Spinner, PricePipe, TradesTable],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [heading]="asset.name">
      <div class="grid gap-6">
        <section>
          <h3 class="mb-2 text-sm font-semibold" i18n="@@investments.trades">Trades</h3>
          <app-trades-table [assetId]="asset.id" [showAsset]="false" />
        </section>
        <section>
          <h3 class="mb-2 text-sm font-semibold" i18n="@@investments.prices">Prices</h3>
          @if (prices.isLoading() && !prices.value()) {
            <div class="flex justify-center py-6"><ui-spinner /></div>
          } @else if (!prices.value()?.length) {
            <ui-empty-state i18n-message="@@investments.noPrices" message="No prices recorded yet." />
          } @else {
            <ui-table>
              <tbody>
                @for (price of prices.value(); track price.id) {
                  <tr>
                    <td class="text-muted">{{ price.occurredAt | date: 'short' }}</td>
                    <td>{{ sideLabel(price.side) }}</td>
                    <td class="text-right tabular-nums">{{ price.price | price: asset.currency }}</td>
                    <td class="text-right">
                      @if (price.source === 'Trade') {
                        <ui-badge i18n="@@price.fromTrade">trade</ui-badge>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </ui-table>
          }
        </section>
      </div>
      <div dialog-actions class="contents">
        <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.close">Close</button>
      </div>
    </ui-dialog>
  `,
})
export class AssetHistoryDialog {
  private readonly api = inject(LedgerApi);
  protected readonly ref = inject(DialogRef);
  protected readonly asset = inject<Asset>(DIALOG_DATA);
  protected readonly sideLabel = priceSideLabel;

  protected readonly prices = rxResource({ stream: () => this.api.assetPrices(this.asset.id) });
}
