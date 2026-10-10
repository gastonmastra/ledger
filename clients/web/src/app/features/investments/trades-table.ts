import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { LedgerApi } from '../../core/api/ledger-api';
import { Trade } from '../../core/api/models';
import { DataChanges } from '../../core/data-changes';
import { tradeKindLabel, tradeStatusLabel } from '../../core/i18n/labels';
import { Badge, BadgeTone, EmptyState, MoneyPipe, Pagination, Spinner, Table } from '../../ui';
import { amountClass } from '../shared/amount';
import { PricePipe, QuantityPipe } from './formatting';

/** My trades (of one asset or all), newest first. */
@Component({
  selector: 'app-trades-table',
  imports: [DatePipe, Table, Badge, EmptyState, Spinner, Pagination, MoneyPipe, PricePipe, QuantityPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (trades.isLoading() && !trades.value()) {
      <div class="flex justify-center py-6"><ui-spinner /></div>
    } @else if (!trades.value()?.items?.length) {
      <ui-empty-state i18n-message="@@investments.noTrades" message="No trades yet." />
    } @else {
      @let result = trades.value()!;
      <ui-table>
        <thead>
          <tr>
            <th i18n="@@investments.date">Date</th>
            @if (showAsset()) {
              <th i18n="@@trade.asset">Asset</th>
            }
            <th i18n="@@investments.trade">Trade</th>
            <th class="text-right" i18n="@@trade.quantity">Quantity</th>
            <th class="text-right" i18n="@@investments.unitPrice">Price</th>
            <th class="text-right" i18n="@@trade.total">Total</th>
            <th class="text-right" i18n="@@investments.realized">Realized</th>
          </tr>
        </thead>
        <tbody>
          @for (trade of result.items; track trade.id) {
            <tr>
              <td class="whitespace-nowrap text-muted">{{ trade.occurredAt | date: 'short' }}</td>
              @if (showAsset()) {
                <td>{{ trade.assetSymbol ?? trade.assetName }}</td>
              }
              <td>
                {{ kindLabel(trade.kind) }}
                @if (trade.status !== 'Completed') {
                  <ui-badge class="ml-1" [tone]="statusTone(trade)" [attr.title]="trade.rejectionReason">{{ statusLabel(trade.status) }}</ui-badge>
                }
              </td>
              <td class="text-right tabular-nums">{{ trade.quantity | quantity }}</td>
              <td class="text-right tabular-nums">{{ trade.unitPrice | price: trade.currency }}</td>
              <td class="text-right tabular-nums">
                {{ trade.total | money: trade.currency }}
                @if (trade.walletAmount && trade.walletCurrency) {
                  <div class="text-xs text-muted">{{ trade.walletAmount | money: trade.walletCurrency }}</div>
                }
              </td>
              <td class="text-right tabular-nums" [class]="amountClass(trade.realizedGain)">
                @if (trade.kind === 'Sell') {
                  {{ trade.realizedGain | money: trade.currency: true }}
                }
              </td>
            </tr>
          }
        </tbody>
      </ui-table>
      @if (result.totalPages > 1) {
        <ui-pagination class="px-4 py-3" [page]="result.page" [totalPages]="result.totalPages" [totalCount]="result.totalCount"
                       (pageChange)="page.set($event)" />
      }
    }
  `,
})
export class TradesTable {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);

  readonly assetId = input<string | null>(null);
  readonly showAsset = input(true);

  protected readonly page = signal(1);
  protected readonly kindLabel = tradeKindLabel;
  protected readonly statusLabel = tradeStatusLabel;
  protected readonly amountClass = amountClass;

  protected readonly trades = rxResource({
    params: () => ({ assetId: this.assetId(), page: this.page(), version: this.changes.version() }),
    stream: ({ params }) => this.api.trades(params.assetId, params.page),
  });

  protected statusTone(trade: Trade): BadgeTone {
    return trade.status === 'Rejected' ? 'danger' : 'warning';
  }
}
