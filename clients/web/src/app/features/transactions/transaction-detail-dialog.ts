import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Transaction } from '../../core/api/models';
import { accountLabel, transactionKindLabel, transactionStatusLabel } from '../../core/i18n/labels';
import { Badge, Button, DialogLayout, Icon, MoneyPipe } from '../../ui';
import { amountClass } from '../shared/amount';

/** What the user chose in the detail: the page runs it with its usual handlers. */
export type TransactionAction = 'edit' | 'delete' | 'operation' | 'history';

export interface TransactionDetailData {
  transaction: Transaction;
  /** Loans, splits and payments are changed in Debts & splits. */
  isDebtOperation: boolean;
}

/** A transaction with all its postings, and what can be done with it. */
@Component({
  selector: 'app-transaction-detail-dialog',
  imports: [DatePipe, DialogLayout, Badge, Button, Icon, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [heading]="t.description || kindLabel(t.kind)">
      <div class="grid gap-4 text-sm">
        <div class="flex flex-wrap items-center gap-2">
          <ui-badge tone="primary">{{ kindLabel(t.kind) }}</ui-badge>
          @if (t.status !== 'Current') {
            <ui-badge [tone]="t.status === 'Deleted' ? 'danger' : 'neutral'">{{ statusLabel(t.status) }}</ui-badge>
          } @else if (t.revision > 1) {
            <ui-badge i18n="@@transactions.edited">Edited</ui-badge>
          }
          <span class="text-muted">{{ t.occurredAt | date: 'medium' }}</span>
        </div>
        @if (t.changeReason) {
          <p class="text-muted">“{{ t.changeReason }}”</p>
        }
        <section>
          <h3 class="mb-2 font-semibold" i18n="@@transactions.postings">Movements</h3>
          <ul class="grid gap-1">
            @for (posting of t.postings; track $index) {
              <li class="flex justify-between gap-4">
                <span class="text-muted">
                  {{ accountLabel(posting) }}
                  @if (posting.counterpartyName) {
                    · {{ posting.counterpartyName }}
                  }
                </span>
                <span class="tabular-nums" [class]="amountClass(posting.amount)">{{ posting.amount | money: posting.currency: true }}</span>
              </li>
            }
          </ul>
        </section>
        @if (data.isDebtOperation) {
          <p class="text-xs text-muted" i18n="@@transactions.openOperationHint">Loans, splits and settlements are changed in Debts & splits.</p>
        }
      </div>
      <div dialog-actions class="contents">
        <button uiButton variant="ghost" type="button" (click)="ref.close('history')">
          <ui-icon name="clock" size="sm" /><ng-container i18n="@@history.title">History</ng-container>
        </button>
        @if (data.isDebtOperation) {
          <button uiButton variant="secondary" type="button" (click)="ref.close('operation')">
            <ui-icon name="scale" size="sm" /><ng-container i18n="@@transactions.openOperation">Operation</ng-container>
          </button>
        }
        @if (t.deletable) {
          <button uiButton variant="secondary" type="button" (click)="ref.close('delete')">
            <ui-icon name="trash" size="sm" /><ng-container i18n="@@ui.delete">Delete</ng-container>
          </button>
        }
        @if (t.editable) {
          <button uiButton type="button" (click)="ref.close('edit')">
            <ui-icon name="pencil" size="sm" /><ng-container i18n="@@ui.edit">Edit</ng-container>
          </button>
        }
      </div>
    </ui-dialog>
  `,
})
export class TransactionDetailDialog {
  protected readonly data = inject<TransactionDetailData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<TransactionAction>>(DialogRef);
  protected readonly t = this.data.transaction;

  protected readonly kindLabel = transactionKindLabel;
  protected readonly statusLabel = transactionStatusLabel;
  protected readonly accountLabel = accountLabel;
  protected readonly amountClass = amountClass;
}
