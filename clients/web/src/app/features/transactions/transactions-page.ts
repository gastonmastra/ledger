import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
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
  ActionsMenu,
  Badge,
  Button,
  Card,
  ConfirmService,
  DialogService,
  EmptyState,
  FilterChip,
  FilterChips,
  Icon,
  MenuAction,
  MoneyPipe,
  PageHeader,
  Pagination,
  RowAction,
  Spinner,
} from '../../ui';
import { amountClass } from '../shared/amount';
import { OperationActions } from '../debts/operation-actions';
import { MovementDialog, MovementDialogData, MovementKind } from './movement-dialog';
import { TransactionAction, TransactionDetailData, TransactionDetailDialog } from './transaction-detail-dialog';
import { TransactionFilters, TransactionFiltersData, TransactionFiltersPanel } from './transaction-filters-panel';
import { TransactionHistoryDialog, TransactionHistoryResult } from './transaction-history-dialog';
import { TransferDialog } from './transfer-dialog';

type Filters = TransactionFilters;

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
    EmptyState,
    Spinner,
    Pagination,
    MoneyPipe,
    ActionsMenu,
    RowAction,
    FilterChips,
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

  /** How many filters are on (Show changes included), for the Filters button. */
  protected readonly activeCount = computed(
    () => Object.values(this.filters()).filter((v) => !!v).length + (this.includeHistory() ? 1 : 0),
  );

  /** The active filters as removable chips. */
  protected readonly chips = computed<FilterChip[]>(() => {
    const f = this.filters();
    const name = <T extends { id: string; name: string }>(items: T[] | undefined, id: string) =>
      items?.find((i) => i.id === id)?.name ?? '…';
    const chips: FilterChip[] = [];

    if (f.walletId) chips.push({ key: 'walletId', label: $localize`:@@transactions.chipWallet:Wallet: ${name(this.wallets.value(), f.walletId)}:name:` });
    if (f.categoryId) chips.push({ key: 'categoryId', label: $localize`:@@transactions.chipCategory:Category: ${name(this.categories.value(), f.categoryId)}:name:` });
    if (f.contactId) chips.push({ key: 'contactId', label: $localize`:@@transactions.chipContact:Contact: ${name(this.contacts.value(), f.contactId)}:name:` });
    if (f.from) chips.push({ key: 'from', label: $localize`:@@transactions.chipFrom:From ${f.from}:date:` });
    if (f.to) chips.push({ key: 'to', label: $localize`:@@transactions.chipTo:To ${f.to}:date:` });
    if (this.includeHistory()) chips.push({ key: 'history', label: $localize`:@@transactions.showChanges:Show changes` });

    return chips;
  });

  protected openFilters(): void {
    void this.dialogs.open<void, TransactionFiltersData>(TransactionFiltersPanel, {
      filters: this.filters,
      includeHistory: this.includeHistory,
      wallets: this.wallets.value() ?? [],
      categories: this.categories.value() ?? [],
      contacts: this.contacts.value() ?? [],
      set: (key, value) => this.setFilter(key, value),
      toggleHistory: () => this.toggleHistory(),
      clear: () => this.clearFilters(),
    }, 'panel');
  }

  /** "Clear all": every filter and Show changes. */
  protected clearAll(): void {
    this.clearFilters();
    this.includeHistory.set(false);
  }

  protected removeChip(key: string): void {
    if (key === 'history') this.toggleHistory();
    else this.setFilter(key as keyof Filters, '');
  }

  /** The wallet movements shown in the row (or every posting when no wallet is involved). */
  protected rowPostings(transaction: Transaction) {
    const wallets = transaction.postings.filter((p) => p.walletId);
    return wallets.length ? wallets : transaction.postings.slice(0, 1);
  }

  protected menu(transaction: Transaction): MenuAction[] {
    return [
      { label: $localize`:@@ui.edit:Edit`, icon: 'pencil', run: () => this.edit(transaction), hidden: !transaction.editable },
      { label: $localize`:@@ui.delete:Delete`, icon: 'trash', run: () => this.remove(transaction), hidden: !transaction.deletable, danger: true },
      {
        label: $localize`:@@transactions.openOperation:Operation`,
        icon: 'scale',
        run: () => this.openOperation(transaction),
        hidden: !this.isDebtOperation(transaction),
        hint: $localize`:@@transactions.openOperationHint:Loans, splits and settlements are changed in Debts & splits.`,
      },
      { label: $localize`:@@history.title:History`, icon: 'clock', run: () => this.history(transaction) },
    ];
  }

  /** The detail pop-up; its buttons run the same actions as the menu. */
  protected async open(transaction: Transaction): Promise<void> {
    const action = await this.dialogs.open<TransactionAction, TransactionDetailData>(
      TransactionDetailDialog,
      { transaction, isDebtOperation: this.isDebtOperation(transaction) },
    );

    switch (action) {
      case 'edit': return this.edit(transaction);
      case 'delete': return this.remove(transaction);
      case 'operation': return this.openOperation(transaction);
      case 'history': return this.history(transaction);
    }
  }

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
    if (transaction.correlationId) void this.operations.open(transaction.correlationId);
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
