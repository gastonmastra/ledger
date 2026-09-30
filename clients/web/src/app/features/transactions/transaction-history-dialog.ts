import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { LedgerApi } from '../../core/api/ledger-api';
import { TransactionHistoryItem, TransactionKind } from '../../core/api/models';
import { accountLabel, changeActionLabel, transactionKindLabel, transactionStatusLabel } from '../../core/i18n/labels';
import { Badge, BadgeTone, Button, DialogLayout, Icon, MoneyPipe, Spinner } from '../../ui';
import { amountClass } from '../shared/amount';

/** Kinds restored from here; the others are restored where they were created (e.g. Debts). */
const RESTORABLE: TransactionKind[] = ['Income', 'Expense', 'Transfer', 'BalanceAdjustment'];

/** Closes with the deleted version to restore, when the user asks for it. */
export type TransactionHistoryResult = { restore: TransactionHistoryItem } | undefined;

/** Every version of a transaction and the reversals that cancelled them. */
@Component({
  selector: 'app-transaction-history-dialog',
  imports: [DatePipe, DialogLayout, Badge, Button, Icon, MoneyPipe, Spinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog i18n-heading="@@history.title" heading="History"
               i18n-description="@@history.transactionDescription" description="Posted entries are never edited: a change cancels the previous version with a reversal and records the new one.">
      @if (history.isLoading() && !history.value()) {
        <div class="flex justify-center py-6"><ui-spinner /></div>
      } @else {
        <ol class="grid gap-2">
          @for (item of history.value(); track item.id) {
            <li class="rounded-ui border border-border p-3 text-sm" [class.ml-6]="item.status === 'Reversal'">
              <div class="flex flex-wrap items-center gap-2">
                <span class="font-medium">{{ kindLabel(item.kind) }}</span>
                @if (item.status !== 'Reversal') {
                  <ui-badge [tone]="tone(item)">{{ statusLabel(item.status) }}</ui-badge>
                }
                <span class="ml-auto text-xs text-muted" i18n="@@history.recordedAt">recorded {{ item.recordedAt | date: 'medium' }}</span>
              </div>
              <p class="mt-1 text-muted">
                {{ item.occurredAt | date: 'medium' }}
                @if (item.description) {
                  · {{ item.description }}
                }
              </p>
              @if (item.action) {
                <p class="mt-1">
                  {{ actionLabel(item.action) }}
                  @if (item.changedByName && !item.changedByMe) {
                    <span class="text-muted" i18n="@@history.byName">by {{ item.changedByName // i18n(ph="name") }}</span>
                  }
                  @if (item.reason) {
                    — “{{ item.reason }}”
                  }
                </p>
              }
              <ul class="mt-2 grid gap-1 text-xs">
                @for (posting of item.postings; track $index) {
                  <li class="flex justify-between gap-4">
                    <span class="text-muted">{{ accountLabel(posting) }}</span>
                    <span class="tabular-nums" [class]="amountClass(posting.amount)">{{ posting.amount | money: posting.currency: true }}</span>
                  </li>
                }
              </ul>
              @if (canRestore(item)) {
                <button uiButton variant="secondary" size="sm" type="button" class="mt-2" (click)="ref.close({ restore: item })">
                  <ui-icon name="undo" size="sm" /><ng-container i18n="@@history.restore">Restore</ng-container>
                </button>
              }
            </li>
          }
        </ol>
      }
      <div dialog-actions class="contents">
        <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.close">Close</button>
      </div>
    </ui-dialog>
  `,
})
export class TransactionHistoryDialog {
  private readonly api = inject(LedgerApi);
  private readonly transactionId = inject<string>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<TransactionHistoryResult>>(DialogRef);

  protected readonly kindLabel = transactionKindLabel;
  protected readonly statusLabel = transactionStatusLabel;
  protected readonly actionLabel = changeActionLabel;
  protected readonly accountLabel = accountLabel;
  protected readonly amountClass = amountClass;

  protected readonly history = rxResource({ stream: () => this.api.transactionHistory(this.transactionId) });

  /** Deleted with nothing newer: it can be brought back. */
  private readonly deletedLatest = computed(() => {
    const items = this.history.value() ?? [];
    if (items.some((i) => i.status === 'Current')) return null;
    return [...items].reverse().find((i) => i.status === 'Deleted') ?? null;
  });

  protected canRestore(item: TransactionHistoryItem): boolean {
    return item === this.deletedLatest() && RESTORABLE.includes(item.kind);
  }

  protected tone(item: TransactionHistoryItem): BadgeTone {
    switch (item.status) {
      case 'Current':
        return 'success';
      case 'Deleted':
        return 'danger';
      default:
        return 'neutral';
    }
  }
}
