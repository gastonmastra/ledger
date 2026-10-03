import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, LOCALE_ID, computed, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { formatDate } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { ASSET_TYPES, Asset, AssetType, CURRENCIES, Currency, TradeKind } from '../../core/api/models';
import { assetTypeLabel } from '../../core/i18n/labels';
import { OperationTracker } from '../../core/operations/operation-tracker';
import { Button, DateTimeInput, DialogLayout, FormField, Input, MoneyPipe, nowIso } from '../../ui';
import { PricePipe, QuantityPipe, tradeTotal } from './formatting';

export interface TradeDialogData {
  kind: TradeKind;
  /** Preselected asset. */
  asset?: Asset;
}

const NEW_ASSET = '__new';

/**
 * Buys an asset with money of a wallet, sells (part of) a holding into a wallet, or records a
 * holding I already owned. Buying can also add a new asset on the way.
 */
@Component({
  selector: 'app-trade-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button, MoneyPipe, DateTimeInput, QuantityPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog [heading]="heading">
        <div class="grid gap-4">
          <ui-form-field i18n-label="@@trade.asset" label="Asset">
            <select uiInput formControlName="assetId">
              <option value="" disabled i18n="@@ui.choose">Choose…</option>
              @for (asset of selectableAssets(); track asset.id) {
                <option [value]="asset.id">
                  {{ asset.name }}{{ asset.symbol ? ' (' + asset.symbol + ')' : '' }} · {{ asset.quantity | quantity }} · {{ asset.currency }}
                </option>
              }
              @if (data.kind !== 'Sell') {
                <option [value]="newAsset" i18n="@@trade.newAsset">+ New asset…</option>
              }
            </select>
          </ui-form-field>

          @if (isNewAsset()) {
            <div class="grid gap-4 rounded-ui border border-border p-3 sm:grid-cols-2">
              <ui-form-field i18n-label="@@asset.name" label="Name">
                <input uiInput formControlName="newName" maxlength="100" i18n-placeholder="@@asset.namePlaceholder" placeholder="e.g. Bitcoin" />
              </ui-form-field>
              <ui-form-field i18n-label="@@asset.symbol" label="Symbol" i18n-hint="@@ui.optional" hint="Optional">
                <input uiInput formControlName="newSymbol" maxlength="20" i18n-placeholder="@@asset.symbolPlaceholder" placeholder="e.g. BTC" />
              </ui-form-field>
              <ui-form-field i18n-label="@@asset.type" label="Type">
                <select uiInput formControlName="newType">
                  @for (type of types; track type) {
                    <option [value]="type">{{ typeLabel(type) }}</option>
                  }
                </select>
              </ui-form-field>
              <ui-form-field i18n-label="@@asset.currency" label="Quoted in"
                             i18n-hint="@@asset.currencyHint" hint="The currency of its price. It can't be changed later.">
                <select uiInput formControlName="newCurrency">
                  @for (currency of currencies; track currency) {
                    <option [value]="currency">{{ currency }}</option>
                  }
                </select>
              </ui-form-field>
            </div>
          }

          <div class="grid gap-4 sm:grid-cols-2">
            <ui-form-field i18n-label="@@trade.quantity" label="Quantity" [hint]="quantityHint()">
              <input uiInput type="number" min="0" step="any" formControlName="quantity" />
            </ui-form-field>
            <ui-form-field [label]="priceLabel" [hint]="priceHint()">
              <input uiInput type="number" min="0" step="any" formControlName="unitPrice" />
            </ui-form-field>
          </div>
          @if (data.kind === 'Sell' && selectedAsset(); as asset) {
            <div class="-mt-2">
              <button uiButton variant="ghost" size="sm" type="button" (click)="sellAll(asset)" i18n="@@trade.sellAll">Sell everything</button>
            </div>
          }

          <p class="text-sm text-muted">
            <span i18n="@@trade.total">Total</span>:
            <span class="font-semibold tabular-nums text-foreground">{{ total() | money: assetCurrency() }}</span>
          </p>

          @if (data.kind !== 'Opening') {
            <div class="grid gap-4 sm:grid-cols-2">
              <ui-form-field [label]="walletLabel">
                <select uiInput formControlName="walletId">
                  <option value="" disabled i18n="@@ui.choose">Choose…</option>
                  @for (wallet of wallets.value(); track wallet.id) {
                    <option [value]="wallet.id">{{ wallet.name }} · {{ wallet.balance | money: wallet.currency }}</option>
                  }
                </select>
              </ui-form-field>
              @if (walletCurrency(); as currency) {
                @if (currency !== assetCurrency()) {
                  <ui-form-field [label]="walletAmountLabel(currency)"
                                 i18n-hint="@@trade.walletAmountHint" hint="The wallet uses another currency: enter what it really moved.">
                    <input uiInput type="number" min="0.01" step="0.01" formControlName="walletAmount" />
                  </ui-form-field>
                }
              }
            </div>
          }

          <ui-form-field [label]="counterPriceLabel" i18n-hint="@@trade.counterPriceHint"
                         hint="Optional. Keeps the value of your holdings up to date.">
            <input uiInput type="number" min="0" step="any" formControlName="counterPrice" />
          </ui-form-field>
          <ui-form-field i18n-label="@@movement.description" label="Description">
            <input uiInput formControlName="description" maxlength="500" />
          </ui-form-field>
          <ui-form-field i18n-label="@@ui.when" label="When" [hint]="dateHint()">
            <ui-datetime-input formControlName="occurredAt" />
          </ui-form-field>
        </div>
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton type="submit" [disabled]="!canSave()" [loading]="saving()">{{ heading }}</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class TradeDialog {
  private readonly api = inject(LedgerApi);
  private readonly tracker = inject(OperationTracker);
  private readonly locale = inject(LOCALE_ID);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly data = inject<TradeDialogData>(DIALOG_DATA);
  protected readonly saving = signal(false);

  protected readonly newAsset = NEW_ASSET;
  protected readonly types = ASSET_TYPES;
  protected readonly currencies = CURRENCIES;
  protected readonly typeLabel = assetTypeLabel;

  protected readonly assets = rxResource({ stream: () => this.api.assets() });
  protected readonly wallets = rxResource({ stream: () => this.api.wallets() });
  private readonly quantityFormat = new QuantityPipe();
  private readonly priceFormat = new PricePipe();

  protected readonly heading = {
    Buy: $localize`:@@trade.buyTitle:Buy`,
    Sell: $localize`:@@trade.sellTitle:Sell`,
    Opening: $localize`:@@trade.openingTitle:Add a holding I already own`,
  }[this.data.kind];

  protected readonly priceLabel =
    this.data.kind === 'Sell'
      ? $localize`:@@trade.sellUnitPrice:Price per unit received`
      : $localize`:@@trade.buyUnitPrice:Price per unit paid`;
  protected readonly counterPriceLabel =
    this.data.kind === 'Sell'
      ? $localize`:@@trade.currentBuyPrice:Current buy price`
      : $localize`:@@trade.currentSellPrice:Current sell price`;
  protected readonly walletLabel =
    this.data.kind === 'Sell' ? $localize`:@@trade.walletIn:Into wallet` : $localize`:@@trade.walletOut:From wallet`;

  protected readonly form = inject(NonNullableFormBuilder).group({
    assetId: [this.data.asset?.id ?? '', Validators.required],
    newName: [''],
    newSymbol: [''],
    newType: ['Stock' as AssetType],
    newCurrency: ['USD' as Currency],
    quantity: [null as number | null, [Validators.required, Validators.min(0)]],
    unitPrice: [null as number | null, [Validators.required, Validators.min(0)]],
    walletId: [''],
    walletAmount: [null as number | null],
    counterPrice: [null as number | null],
    description: [''],
    occurredAt: [nowIso(), Validators.required],
  });

  private readonly value = toSignal(this.form.valueChanges, { initialValue: this.form.value });

  protected readonly selectableAssets = computed(() => {
    const assets = this.assets.value() ?? [];
    return this.data.kind === 'Sell' ? assets.filter((a) => a.quantity > 0) : assets;
  });

  protected readonly isNewAsset = computed(() => this.value().assetId === NEW_ASSET);

  protected readonly selectedAsset = computed(
    () => this.assets.value()?.find((a) => a.id === this.value().assetId) ?? this.data.asset ?? null,
  );

  protected readonly assetCurrency = computed<Currency>(() =>
    this.isNewAsset() ? (this.value().newCurrency ?? 'USD') : (this.selectedAsset()?.currency ?? 'USD'),
  );

  protected readonly walletCurrency = computed(
    () => this.wallets.value()?.find((w) => w.id === this.value().walletId)?.currency ?? null,
  );

  protected readonly crossCurrency = computed(
    () => this.data.kind !== 'Opening' && !!this.walletCurrency() && this.walletCurrency() !== this.assetCurrency(),
  );

  protected readonly total = computed(() => tradeTotal(this.value().quantity, this.value().unitPrice));

  protected readonly quantityHint = computed(() => {
    const asset = this.selectedAsset();
    return asset && !this.isNewAsset()
      ? $localize`:@@trade.youHold:You hold ${this.quantityFormat.transform(asset.quantity)}:quantity:`
      : null;
  });

  protected readonly priceHint = computed(() => {
    const asset = this.selectedAsset();
    if (!asset || this.isNewAsset()) return null;

    const latest = this.data.kind === 'Sell' ? asset.sellPrice : asset.buyPrice;
    return latest ? $localize`:@@trade.latestPrice:Latest: ${this.priceFormat.transform(latest, asset.currency)}:price:` : null;
  });

  protected readonly dateHint = computed(() => {
    const last = this.selectedAsset()?.lastTradeAt;
    return last && !this.isNewAsset()
      ? $localize`:@@trade.notBefore:Not before its latest trade (${formatDate(last, 'medium', this.locale)}:date:).`
      : null;
  });

  protected readonly canSave = computed(() => {
    const value = this.value();
    const assetChosen = this.isNewAsset() ? !!value.newName?.trim() : !!value.assetId;

    return (
      assetChosen &&
      (value.quantity ?? 0) > 0 &&
      (value.unitPrice ?? 0) > 0 &&
      this.total() > 0 &&
      (this.data.kind === 'Opening' || !!value.walletId) &&
      (!this.crossCurrency() || (value.walletAmount ?? 0) > 0)
    );
  });

  protected walletAmountLabel(currency: Currency): string {
    return this.data.kind === 'Sell'
      ? $localize`:@@trade.walletAmountIn:Received in ${currency}:currency:`
      : $localize`:@@trade.walletAmountOut:Paid in ${currency}:currency:`;
  }

  protected sellAll(asset: Asset): void {
    this.form.controls.quantity.setValue(asset.quantity);
  }

  protected async save(): Promise<void> {
    if (!this.canSave()) return;

    const value = this.form.getRawValue();
    this.saving.set(true);

    try {
      let assetId = value.assetId;

      if (this.isNewAsset()) {
        const asset = await firstValueFrom(
          this.api.createAsset({
            name: value.newName.trim(),
            symbol: value.newSymbol.trim() || null,
            type: value.newType,
            currency: value.newCurrency,
          }),
        );
        assetId = asset.id;
      }

      const counter = value.counterPrice && value.counterPrice > 0 ? value.counterPrice : null;

      await this.tracker.submitTrade(
        this.api.recordTrade(this.data.kind, {
          assetId,
          quantity: value.quantity!,
          unitPrice: value.unitPrice!,
          walletId: this.data.kind === 'Opening' ? null : value.walletId,
          walletAmount: this.crossCurrency() ? value.walletAmount : null,
          sellPrice: this.data.kind === 'Sell' ? null : counter,
          buyPrice: this.data.kind === 'Sell' ? counter : null,
          description: value.description.trim() || null,
          occurredAt: value.occurredAt,
        }),
        {
          Buy: $localize`:@@trade.bought:Purchase recorded.`,
          Sell: $localize`:@@trade.sold:Sale recorded.`,
          Opening: $localize`:@@trade.opened:Holding recorded.`,
        }[this.data.kind],
      );
      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}
