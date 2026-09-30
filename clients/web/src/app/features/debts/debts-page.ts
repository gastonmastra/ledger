import { ChangeDetectionStrategy, Component, LOCALE_ID, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { LedgerApi } from '../../core/api/ledger-api';
import { ContactDebts, Debt } from '../../core/api/models';
import { CurrentUser } from '../../core/current-user';
import { DataChanges } from '../../core/data-changes';
import { debtStatusLabel } from '../../core/i18n/labels';
import { OperationTracker } from '../../core/operations/operation-tracker';
import { Badge, Button, Card, DialogService, EmptyState, Icon, MoneyPipe, PageHeader, Spinner, Table } from '../../ui';
import { amountClass } from '../shared/amount';
import { SplitDialog, SplitDialogData } from '../shared/split-dialog';
import { WalletPickerDialog, WalletPickerData } from '../shared/wallet-picker-dialog';
import { FriendPaidDialog, LendDialog } from './debt-dialogs';

@Component({
  selector: 'app-debts-page',
  imports: [PageHeader, Card, Table, Badge, Button, Icon, EmptyState, Spinner, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './debts-page.html',
})
export class DebtsPage {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);
  private readonly dialogs = inject(DialogService);
  private readonly tracker = inject(OperationTracker);
  private readonly currentUser = inject(CurrentUser);
  private readonly locale = inject(LOCALE_ID);

  protected readonly statusLabel = debtStatusLabel;
  protected readonly amountClass = amountClass;

  protected readonly debts = rxResource({
    params: () => this.changes.version(),
    stream: () => this.api.debts(),
  });

  private readonly contacts = rxResource({
    params: () => this.changes.version(),
    stream: () => this.api.contacts(),
  });

  protected readonly settleable = computed(() =>
    (this.debts.value()?.debts ?? []).filter((d) => d.groupId === null && d.status === 'Open' && !d.isSettling),
  );

  protected openDialog(kind: 'lend' | 'friend'): void {
    void this.dialogs.open(kind === 'lend' ? LendDialog : FriendPaidDialog);
  }

  protected splitWithContacts(): void {
    const me = this.currentUser.me.value();
    if (!me) return;

    void this.dialogs.open<boolean, SplitDialogData>(SplitDialog, {
      groupId: null,
      people: [
        { personId: me.personId, name: me.displayName, isMe: true },
        ...(this.contacts.value() ?? []).map((c) => ({ personId: c.personId, name: c.name, isMe: false })),
      ],
    }, 'lg');
  }

  protected async settleDebt(debt: Debt): Promise<void> {
    const receiving = debt.direction === 'OwedToMe';
    const walletId = await this.pickWallet(debt.currency, receiving, debt.counterpartyName, debt.amount);

    if (walletId) {
      await this.tracker.submit(
        this.api.settle({ scope: 'Debt', debtId: debt.id, walletId }),
        $localize`:@@debts.settled:Debt with ${debt.counterpartyName}:name: settled.`,
      );
    }
  }

  protected async settleContact(balance: ContactDebts): Promise<void> {
    const contact = this.contacts.value()?.find((c) => c.personId === balance.personId);
    if (!contact) return;

    let walletId: string | null = null;

    if (balance.net !== 0) {
      walletId =
        (await this.pickWallet(balance.currency, balance.net > 0, balance.name, Math.abs(balance.net))) ?? null;
      if (!walletId) return;
    }

    await this.tracker.submit(
      this.api.settle({ scope: 'Contact', contactId: contact.id, currency: balance.currency, walletId }),
      $localize`:@@debts.settledUp:All settled with ${balance.name}:name:.`,
    );
  }

  private pickWallet(currency: Debt['currency'], receiving: boolean, name: string, amount: number) {
    const formatted = new Intl.NumberFormat(this.locale, { style: 'currency', currency }).format(amount);

    return this.dialogs.open<string, WalletPickerData>(WalletPickerDialog, {
      currency,
      title: $localize`:@@debts.settleTitle:Settle with ${name}:name:`,
      message: receiving
        ? $localize`:@@debts.settleReceive:${name}:name: pays you ${formatted}:amount:. Where did the money go?`
        : $localize`:@@debts.settlePay:You pay ${name}:name: ${formatted}:amount:. Where does the money come from?`,
      confirmLabel: $localize`:@@debts.settle:Settle`,
    });
  }
}
