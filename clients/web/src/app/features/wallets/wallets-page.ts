import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { Wallet } from '../../core/api/models';
import { DataChanges } from '../../core/data-changes';
import { walletStatusLabel } from '../../core/i18n/labels';
import { ToastService } from '../../core/notifications/toast.service';
import {
  ActionsMenu,
  Badge,
  Button,
  Card,
  ConfirmService,
  DialogService,
  EmptyState,
  Icon,
  MenuAction,
  MoneyPipe,
  PageHeader,
  RowAction,
  Spinner,
  Table,
} from '../../ui';
import { NameDialog, NameDialogData } from '../shared/name-dialog';
import { AdjustBalanceDialog, OpeningBalanceDialog } from './balance-dialogs';
import { WalletAction, WalletDetailDialog } from './wallet-detail-dialog';
import { WalletDialog } from './wallet-dialog';

@Component({
  selector: 'app-wallets-page',
  imports: [PageHeader, Card, Table, Badge, Button, Icon, EmptyState, Spinner, MoneyPipe, ActionsMenu, RowAction],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './wallets-page.html',
})
export class WalletsPage {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);
  private readonly dialogs = inject(DialogService);
  private readonly confirm = inject(ConfirmService);
  private readonly toasts = inject(ToastService);

  protected readonly showArchived = signal(false);
  protected readonly statusLabel = walletStatusLabel;

  protected readonly wallets = rxResource({
    params: () => ({ archived: this.showArchived(), version: this.changes.version() }),
    stream: ({ params }) => this.api.wallets(params.archived),
  });

  constructor() {
    // New wallets are set up asynchronously by the ledger: refresh until they are ready.
    effect((onCleanup) => {
      if (this.wallets.value()?.some((w) => !w.isReady)) {
        const timer = setTimeout(() => this.wallets.reload(), 1000);
        onCleanup(() => clearTimeout(timer));
      }
    });
  }

  /** The detail pop-up; its buttons run the same actions as the menu. */
  protected async open(wallet: Wallet): Promise<void> {
    const action = await this.dialogs.open<WalletAction, Wallet>(WalletDetailDialog, wallet);

    switch (action) {
      case 'opening': return this.openingBalance(wallet);
      case 'adjust': return this.adjust(wallet);
      case 'rename': return this.rename(wallet);
      case 'archive': return this.archive(wallet);
      case 'delete': return this.remove(wallet);
    }
  }

  protected menu(wallet: Wallet): MenuAction[] {
    const ready = wallet.isReady;

    return [
      { label: $localize`:@@wallets.openingAction:Opening balance`, run: () => this.openingBalance(wallet), hidden: !ready },
      { label: $localize`:@@wallets.adjustAction:Adjust balance`, run: () => this.adjust(wallet), hidden: !ready },
      { label: $localize`:@@ui.rename:Rename`, icon: 'pencil', run: () => this.rename(wallet) },
      { label: $localize`:@@wallets.archive:Archive`, icon: 'archive', run: () => this.archive(wallet) },
      { label: $localize`:@@ui.delete:Delete`, icon: 'trash', run: () => this.remove(wallet), danger: true },
    ];
  }

  protected async create(): Promise<void> {
    if (await this.dialogs.open<boolean>(WalletDialog)) {
      this.toasts.success($localize`:@@wallets.created:Wallet created.`);
      this.changes.notify();
    }
  }

  protected async rename(wallet: Wallet): Promise<void> {
    const name = await this.dialogs.open<string, NameDialogData>(NameDialog, {
      title: $localize`:@@wallets.rename:Rename wallet`,
      label: $localize`:@@wallets.name:Name`,
      value: wallet.name,
      maxLength: 100,
    });

    if (name && name !== wallet.name) {
      await firstValueFrom(this.api.renameWallet(wallet.id, name));
      this.changes.notify();
    }
  }

  protected async openingBalance(wallet: Wallet): Promise<void> {
    if (await this.dialogs.open<boolean, Wallet>(OpeningBalanceDialog, wallet)) {
      this.toasts.success($localize`:@@opening.saved:Opening balance updated.`);
      this.changes.notify();
    }
  }

  protected async adjust(wallet: Wallet): Promise<void> {
    if (await this.dialogs.open<boolean, Wallet>(AdjustBalanceDialog, wallet)) {
      this.toasts.success($localize`:@@adjust.saved:Balance adjusted.`);
      this.changes.notify();
    }
  }

  protected async archive(wallet: Wallet): Promise<void> {
    const confirmed = await this.confirm.confirm({
      title: $localize`:@@wallets.archiveTitle:Archive wallet`,
      message: $localize`:@@wallets.archiveMessage:"${wallet.name}:name:" will be hidden. Its history is kept. Only wallets with a zero balance can be archived.`,
      confirmLabel: $localize`:@@wallets.archive:Archive`,
    });

    if (confirmed) {
      await firstValueFrom(this.api.archiveWallet(wallet.id));
      this.toasts.success($localize`:@@wallets.archived:Wallet archived.`);
      this.changes.notify();
    }
  }

  protected async remove(wallet: Wallet): Promise<void> {
    const confirmed = await this.confirm.confirm({
      title: $localize`:@@wallets.deleteTitle:Delete wallet`,
      message: $localize`:@@wallets.deleteMessage:"${wallet.name}:name:" will be deleted. Only wallets without transactions can be deleted.`,
      confirmLabel: $localize`:@@ui.delete:Delete`,
      danger: true,
    });

    if (confirmed) {
      await firstValueFrom(this.api.deleteWallet(wallet.id));
      this.toasts.success($localize`:@@wallets.deleted:Wallet deleted.`);
      this.changes.notify();
    }
  }
}
