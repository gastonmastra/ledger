import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { LedgerApi } from '../../core/api/ledger-api';
import { Transaction, TransactionFilter } from '../../core/api/models';
import { DataChanges } from '../../core/data-changes';
import { accountLabel, transactionKindLabel, transactionStatusLabel } from '../../core/i18n/labels';
import { ToastService } from '../../core/notifications/toast.service';
import { firstValueFrom } from 'rxjs';
import {
  Badge,
  Button,
  Card,
  ConfirmService,
  DialogService,
  EmptyState,
  FormField,
  Icon,
  Input,
  MoneyPipe,
  PageHeader,
  Pagination,
  Spinner,
} from '../../ui';
import { amountClass } from '../shared/amount';
import { OperationActions } from '../debts/operation-actions';
import { MovementDialog, MovementDialogData, MovementKind } from './movement-dialog';
import { TransactionHistoryDialog, TransactionHistoryResult } from './transaction-history-dialog';
import { TransferDialog } from './transfer-dialog';

interface Filters {
  walletId: string;
  categoryId: string;
  contactId: string;
  from: string;
  to: string;
}

const EMPTY_FILTERS: Filters = { walletId: '', categoryId: '', contactId: '', from: '', to: '' };
const PAGE_SIZE = 15;

@Component({
  selector: 'app-transactions-page',
  imports: [
    FormsModule,
    DatePipe,
    PageHeader,
    Card,
    Badge,
    Button,
    Icon,
    Input,
    FormField,
    EmptyState,
    Spinner,
    Pagination,
    MoneyPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './transactions-page.html',
})
export class TransactionsPage {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);
  private readonly dialogs = inject(DialogService);
  private readonly toasts = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly operations = inject(OperationActions);

  protected readonly filters = signal<Filters>({ ...EMPTY_FILTERS });
  protected readonly page = signal(1);
  /** Also show changed and deleted versions and their reversals. */
  protected readonly includeHistory = signal(false);
  protected readonly kindLabel = transactionKindLabel;
  protected readonly statusLabel = transactionStatusLabel;
  protected readonly accountLabel = accountLabel;
  protected readonly amountClass = amountClass;

  protected readonly wallets = rxResource({ stream: () => this.api.wallets(true) });
  protected readonly categories = rxResource({ stream: () => this.api.categories(true) });
  protected readonly contacts = rxResource({ stream: () => this.api.contacts() });

  protected readonly transactions = rxResource({
    params: () => ({
      filters: this.filters(),
      page: this.page(),
      includeHistory: this.includeHistory(),
      version: this.changes.version(),
    }),
    stream: ({ params }) => this.api.transactions({ ...toQuery(params.filters, params.page), includeHistory: params.includeHistory }),
  });

  protected toggleHistory(): void {
    this.includeHistory.update((v) => !v);
    this.page.set(1);
  }

  /** Loans, splits, settlements and existing debts are changed in Debts & splits. */
  protected isDebtOperation(transaction: Transaction): boolean {
    return (
      !!transaction.correlationId &&
      ['Loan', 'Split', 'Settlement', 'InitialDebt', 'WalletAssignment'].includes(transaction.kind)
    );
  }

  protected async edit(transaction: Transaction): Promise<void> {
    const saved =
      transaction.kind === 'Transfer'
        ? await this.dialogs.open<boolean, Transaction>(TransferDialog, transaction)
        : await this.dialogs.open<boolean, MovementDialogData>(MovementDialog, {
            kind: transaction.kind === 'Income' ? 'income' : 'expense',
            edit: transaction,
          });

    if (saved) {
      this.toasts.success($localize`:@@transactions.corrected:Corrected. The previous version is kept in the history.`);
      this.changes.notify();
    }
  }

  protected async remove(transaction: Transaction): Promise<void> {
    const reason = await this.confirm.askReason({
      title: $localize`:@@transactions.deleteTitle:Delete this transaction?`,
      message: $localize`:@@transactions.deleteMessage:A reversal cancels it; it stays in the history and can be restored.`,
      confirmLabel: $localize`:@@ui.delete:Delete`,
      danger: true,
    });

    if (reason === null) return;

    await firstValueFrom(this.api.deleteTransaction(transaction.id, reason));
    this.toasts.success($localize`:@@transactions.deleted:Transaction deleted.`);
    this.changes.notify();
  }

  protected async history(transaction: Transaction): Promise<void> {
    const result = await this.dialogs.open<TransactionHistoryResult, string>(TransactionHistoryDialog, transaction.id, 'lg');
    if (!result?.restore) return;

    const reason = await this.confirm.askReason({
      title: $localize`:@@transactions.restoreTitle:Restore this transaction?`,
      message: $localize`:@@transactions.restoreMessage:It counts again as it was before it was deleted.`,
      confirmLabel: $localize`:@@history.restore:Restore`,
    });

    if (reason === null) return;

    await firstValueFrom(this.api.restoreTransaction(result.restore.id, reason));
    this.toasts.success($localize`:@@transactions.restored:Transaction restored.`);
    this.changes.notify();
  }

  /** Opens the loan, split or settlement the transaction belongs to. */
  protected openOperation(transaction: Transaction): void {
    if (transaction.correlationId) void this.operations.history(transaction.correlationId);
  }

  protected setFilter(key: keyof Filters, value: string): void {
    this.filters.update((f) => ({ ...f, [key]: value }));
    this.page.set(1);
  }

  protected clearFilters(): void {
    this.filters.set({ ...EMPTY_FILTERS });
    this.page.set(1);
  }

  protected async record(kind: MovementKind): Promise<void> {
    if (await this.dialogs.open<boolean, MovementKind>(MovementDialog, kind)) {
      this.toasts.success(
        kind === 'income'
          ? $localize`:@@movement.incomeSaved:Income recorded.`
          : $localize`:@@movement.expenseSaved:Expense recorded.`,
      );
      this.changes.notify();
    }
  }

  protected async transfer(): Promise<void> {
    if (await this.dialogs.open<boolean>(TransferDialog)) {
      this.toasts.success($localize`:@@transfer.saved:Transfer recorded.`);
      this.changes.notify();
    }
  }
}

function toQuery(filters: Filters, page: number): TransactionFilter {
  return {
    page,
    pageSize: PAGE_SIZE,
    walletId: filters.walletId || null,
    categoryId: filters.categoryId || null,
    contactId: filters.contactId || null,
    // Dates are inclusive days in the user's time zone.
    from: filters.from ? new Date(`${filters.from}T00:00:00`).toISOString() : null,
    to: filters.to ? new Date(`${filters.to}T23:59:59.999`).toISOString() : null,
  };
}
