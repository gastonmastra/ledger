import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { Asset, TradeKind } from '../../core/api/models';
import { DataChanges } from '../../core/data-changes';
import { assetTypeLabel, valuationSourceLabel } from '../../core/i18n/labels';
import { ToastService } from '../../core/notifications/toast.service';
import {
  Badge,
  Button,
  Card,
  ConfirmService,
  DialogService,
  EmptyState,
  Icon,
  MoneyPipe,
  PageHeader,
  Spinner,
  Stat,
  Table,
} from '../../ui';
import { amountClass, amountTone } from '../shared/amount';
import { AssetDialog } from './asset-dialog';
import { AssetHistoryDialog } from './asset-history-dialog';
import { PricePipe, QuantityPipe } from './formatting';
import { PriceDialog } from './price-dialog';
import { TradeDialog, TradeDialogData } from './trade-dialog';
import { TradesTable } from './trades-table';

/** My assets and holdings, valued at the latest sell price, and every trade. */
@Component({
  selector: 'app-investments-page',
  imports: [
    PageHeader,
    Card,
    Stat,
    Table,
    Badge,
    Button,
    Icon,
    EmptyState,
    Spinner,
    MoneyPipe,
    PricePipe,
    QuantityPipe,
    TradesTable,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './investments-page.html',
})
export class InvestmentsPage {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);
  private readonly dialogs = inject(DialogService);
  private readonly confirm = inject(ConfirmService);
  private readonly toasts = inject(ToastService);

  protected readonly showArchived = signal(false);
  protected readonly typeLabel = assetTypeLabel;
  protected readonly valuationLabel = valuationSourceLabel;
  protected readonly amountTone = amountTone;
  protected readonly amountClass = amountClass;

  protected readonly assets = rxResource({
    params: () => ({ archived: this.showArchived(), version: this.changes.version() }),
    stream: ({ params }) => this.api.assets(params.archived),
  });

  protected readonly totals = rxResource({
    params: () => this.changes.version(),
    stream: () => this.api.investmentsSummary(),
  });

  constructor() {
    // Trades are recorded asynchronously: refresh while one is in progress.
    effect((onCleanup) => {
      if (this.assets.value()?.some((a) => a.hasPendingTrade)) {
        const timer = setTimeout(() => this.changes.notify(), 1500);
        onCleanup(() => clearTimeout(timer));
      }
    });
  }

  protected async trade(kind: TradeKind, asset?: Asset): Promise<void> {
    await this.dialogs.open<boolean, TradeDialogData>(TradeDialog, { kind, asset }, 'lg');
  }

  protected async create(): Promise<void> {
    if (await this.dialogs.open(AssetDialog)) {
      this.toasts.success($localize`:@@asset.created:Asset added.`);
      this.changes.notify();
    }
  }

  protected async edit(asset: Asset): Promise<void> {
    if (await this.dialogs.open<boolean, Asset>(AssetDialog, asset)) {
      this.changes.notify();
    }
  }

  protected async updatePrice(asset: Asset): Promise<void> {
    if (await this.dialogs.open<boolean, Asset>(PriceDialog, asset)) {
      this.toasts.success($localize`:@@price.saved:Price recorded.`);
      this.changes.notify();
    }
  }

  protected history(asset: Asset): void {
    void this.dialogs.open(AssetHistoryDialog, asset, 'lg');
  }

  protected async archive(asset: Asset): Promise<void> {
    const confirmed = await this.confirm.confirm({
      title: $localize`:@@asset.archiveTitle:Archive asset`,
      message: $localize`:@@asset.archiveMessage:"${asset.name}:name:" will be hidden. Its trades are kept. Only assets you no longer hold can be archived.`,
      confirmLabel: $localize`:@@wallets.archive:Archive`,
    });

    if (confirmed) {
      await firstValueFrom(this.api.archiveAsset(asset.id));
      this.toasts.success($localize`:@@asset.archived:Asset archived.`);
      this.changes.notify();
    }
  }
}
