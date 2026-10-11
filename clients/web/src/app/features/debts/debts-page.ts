import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { LedgerApi } from '../../core/api/ledger-api';
import { ContactDebts, Debt } from '../../core/api/models';
import { CurrentUser } from '../../core/current-user';
import { DataChanges } from '../../core/data-changes';
import { debtStatusLabel } from '../../core/i18n/labels';
import { Badge, Button, Card, DialogService, EmptyState, Icon, MoneyPipe, PageHeader, Spinner, Table } from '../../ui';
import { amountClass } from '../shared/amount';
import { SplitDialog, SplitDialogData } from '../shared/split-dialog';
import { FriendPaidDialog, LendDialog } from './debt-dialogs';
import { DebtPaymentsDialog } from './debt-payments-dialog';
import { InitialDebtDialog } from './initial-debt-dialog';
import { OperationsList } from './operations-list';
import { PaymentDialog, PaymentDialogData } from './payment-dialog';

@Component({
  selector: 'app-debts-page',
  imports: [RouterLink, PageHeader, Card, Table, Badge, Button, Icon, EmptyState, Spinner, MoneyPipe, OperationsList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './debts-page.html',
})
export class DebtsPage {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);
  private readonly dialogs = inject(DialogService);
  private readonly currentUser = inject(CurrentUser);

  protected readonly statusLabel = debtStatusLabel;
  protected readonly amountClass = amountClass;

  protected readonly openHeading = $localize`:@@debts.open:Open debts`;
  protected readonly historyHeading = $localize`:@@debts.withPaid:Open and paid debts`;

  /** Also show the debts that were fully paid (history). */
  protected readonly showPaid = signal(false);

  protected readonly debts = rxResource({
    params: () => ({ version: this.changes.version(), showPaid: this.showPaid() }),
    stream: ({ params }) => this.api.debts(params.showPaid),
  });

  /** Changes of shared operations waiting for my answer. */
  protected readonly toReview = rxResource({
    params: () => this.changes.version(),
    stream: () => this.api.changes(),
  });

  private readonly contacts = rxResource({
    params: () => this.changes.version(),
    stream: () => this.api.contacts(),
  });

  protected openDialog(kind: 'lend' | 'friend' | 'initial'): void {
    const dialogs = { lend: LendDialog, friend: FriendPaidDialog, initial: InitialDebtDialog };
    void this.dialogs.open(dialogs[kind]);
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

  /** Records a payment with anybody (no person chosen yet). */
  protected recordPayment(): void {
    void this.dialogs.open<boolean, PaymentDialogData>(PaymentDialog, {}, 'lg');
  }

  /** Records a payment with a person, pre-filled to pay everything that is owed between us. */
  protected payBalance(balance: ContactDebts): void {
    const contact = this.contacts.value()?.find((c) => c.personId === balance.personId);
    if (!contact) return;

    void this.dialogs.open<boolean, PaymentDialogData>(PaymentDialog, {
      contactId: contact.id,
      currency: balance.currency,
      direction: balance.net >= 0 ? 'TheyPaidMe' : 'IPaidThem',
      amount: Math.abs(balance.net) || undefined,
    }, 'lg');
  }

  protected showPayments(debt: Debt): void {
    void this.dialogs.open<void, Debt>(DebtPaymentsDialog, debt);
  }
}
