import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Asset } from '../../core/api/models';
import { assetTypeLabel, valuationSourceLabel } from '../../core/i18n/labels';
import { Badge, Button, DialogLayout, Icon, MoneyPipe } from '../../ui';
import { amountClass } from '../shared/amount';
import { PricePipe, QuantityPipe } from './formatting';

export type AssetAction = 'buy' | 'sell' | 'price' | 'history' | 'edit' | 'archive';

/** An asset: what I hold, its cost, its value and gains, and what can be done with it. */
@Component({
  selector: 'app-asset-detail-dialog',
  imports: [DatePipe, DialogLayout, Badge, Button, Icon, MoneyPipe, PricePipe, QuantityPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [heading]="asset.name" [description]="asset.symbol">
      <div class="grid gap-4 text-sm">
        <div class="flex flex-wrap items-center gap-2">
          <ui-badge>{{ typeLabel(asset.type) }}</ui-badge>
          <ui-badge>{{ asset.currency }}</ui-badge>
          @if (asset.hasPendingTrade) {
            <ui-badge tone="warning" i18n="@@opStatus.pending">Being recorded</ui-badge>
          }
          @if (asset.status === 'Archived') {
            <ui-badge i18n="@@walletStatus.archived">Archived</ui-badge>
          }
        </div>
        <dl class="grid grid-cols-2 gap-x-4 gap-y-2">
          <dt class="text-muted" i18n="@@trade.quantity">Quantity</dt>
          <dd class="text-right tabular-nums">{{ asset.quantity | quantity }}</dd>
          <dt class="text-muted" i18n="@@investments.avgCost">Average cost</dt>
          <dd class="text-right tabular-nums">{{ asset.averageCost | price: asset.currency }}</dd>
          <dt class="text-muted" i18n="@@assetDetail.costBasis">Cost</dt>
          <dd class="text-right tabular-nums">{{ asset.costBasis | money: asset.currency }}</dd>
          @if (asset.valuationSource) {
            <dt class="text-muted">{{ valuationLabel(asset.valuationSource) }}</dt>
            <dd class="text-right tabular-nums">{{ asset.valuationPrice | price: asset.currency }}</dd>
          }
          <dt class="text-muted" i18n="@@investments.value">Market value</dt>
          <dd class="text-right font-semibold tabular-nums">{{ asset.marketValue | money: asset.currency }}</dd>
          <dt class="text-muted" i18n="@@investments.unrealized">Unrealized gain</dt>
          <dd class="text-right tabular-nums" [class]="amountClass(asset.unrealizedGain)">{{ asset.unrealizedGain | money: asset.currency: true }}</dd>
          <dt class="text-muted" i18n="@@assetDetail.realized">Realized gain</dt>
          <dd class="text-right tabular-nums" [class]="amountClass(asset.realizedGain)">{{ asset.realizedGain | money: asset.currency: true }}</dd>
          @if (asset.lastTradeAt) {
            <dt class="text-muted" i18n="@@assetDetail.lastTrade">Last trade</dt>
            <dd class="text-right">{{ asset.lastTradeAt | date: 'mediumDate' }}</dd>
          }
        </dl>
        @if (active) {
          <div class="grid grid-cols-2 gap-2 sm:grid-cols-3">
            <button uiButton type="button" (click)="ref.close('buy')" i18n="@@trade.buyTitle">Buy</button>
            @if (asset.quantity > 0) {
              <button uiButton variant="secondary" type="button" (click)="ref.close('sell')" i18n="@@trade.sellTitle">Sell</button>
            }
            <button uiButton variant="secondary" type="button" (click)="ref.close('price')" i18n="@@price.title">Update price</button>
          </div>
        }
      </div>
      <div dialog-actions class="contents">
        <button uiButton variant="ghost" type="button" (click)="ref.close('history')">
          <ui-icon name="clock" size="sm" /><ng-container i18n="@@history.title">History</ng-container>
        </button>
        @if (active && asset.quantity === 0 && !asset.hasPendingTrade) {
          <button uiButton variant="secondary" type="button" (click)="ref.close('archive')">
            <ui-icon name="archive" size="sm" /><ng-container i18n="@@wallets.archive">Archive</ng-container>
          </button>
        }
        @if (active) {
          <button uiButton variant="secondary" type="button" (click)="ref.close('edit')">
            <ui-icon name="pencil" size="sm" /><ng-container i18n="@@ui.edit">Edit</ng-container>
          </button>
        }
      </div>
    </ui-dialog>
  `,
})
export class AssetDetailDialog {
  protected readonly asset = inject<Asset>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<AssetAction>>(DialogRef);
  protected readonly active = this.asset.status === 'Active';
  protected readonly typeLabel = assetTypeLabel;
  protected readonly valuationLabel = valuationSourceLabel;
  protected readonly amountClass = amountClass;
}
