import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { LedgerApi } from '../../core/api/ledger-api';
import { TransactionFilter } from '../../core/api/models';
import { DataChanges } from '../../core/data-changes';
import { accountLabel, transactionKindLabel } from '../../core/i18n/labels';
import { ToastService } from '../../core/notifications/toast.service';
import {
  Badge,
  Button,
  Card,
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
import { MovementDialog, MovementKind } from './movement-dialog';
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

  protected readonly filters = signal<Filters>({ ...EMPTY_FILTERS });
  protected readonly page = signal(1);
  protected readonly kindLabel = transactionKindLabel;
  protected readonly accountLabel = accountLabel;
  protected readonly amountClass = amountClass;

  protected readonly wallets = rxResource({ stream: () => this.api.wallets(true) });
  protected readonly categories = rxResource({ stream: () => this.api.categories(true) });
  protected readonly contacts = rxResource({ stream: () => this.api.contacts() });

  protected readonly transactions = rxResource({
    params: () => ({ filters: this.filters(), page: this.page(), version: this.changes.version() }),
    stream: ({ params }) => this.api.transactions(toQuery(params.filters, params.page)),
  });

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
