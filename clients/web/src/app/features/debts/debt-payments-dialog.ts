import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { LedgerApi } from '../../core/api/ledger-api';
import { Debt } from '../../core/api/models';
import { Badge, Button, DialogLayout, EmptyState, MoneyPipe, Spinner } from '../../ui';

/** The payments that paid (part of) a debt, oldest first. */
@Component({
  selector: 'app-debt-payments-dialog',
  imports: [DatePipe, DialogLayout, Badge, Button, EmptyState, MoneyPipe, Spinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog i18n-heading="@@debtPayments.title" heading="Payments of this debt"
               [description]="debt.description ?? debt.counterpartyName">
      <p class="mb-3 text-sm">
        <span i18n="@@debtPayments.summary">{{ debt.paid | money: debt.currency // i18n(ph="paid") }} paid of {{ debt.amount | money: debt.currency // i18n(ph="amount") }}</span>
        ·
        <span class="font-medium" i18n="@@debtPayments.left">{{ debt.remaining | money: debt.currency // i18n(ph="left") }} left</span>
      </p>
      @if (payments.isLoading() && !payments.value()) {
        <div class="flex justify-center py-4"><ui-spinner /></div>
      } @else if (!payments.value()?.length) {
        <ui-empty-state i18n-message="@@debtPayments.none" message="Nothing paid yet." />
      } @else {
        <ul class="grid gap-2">
          @for (payment of payments.value(); track payment.settlementOperationId) {
            <li class="flex flex-wrap items-center gap-2 rounded-ui border border-border p-3 text-sm">
              <span class="flex-1">
                {{ payment.description }}
                <span class="text-xs text-muted">· {{ payment.occurredAt | date: 'medium' }}</span>
              </span>
              @if (payment.status === 'Pending') {
                <ui-badge tone="warning" i18n="@@debtStatus.settling">Settling</ui-badge>
              }
              <span class="font-medium tabular-nums">{{ payment.amount | money: debt.currency }}</span>
            </li>
          }
        </ul>
      }
      <div dialog-actions class="contents">
        <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.close">Close</button>
      </div>
    </ui-dialog>
  `,
})
export class DebtPaymentsDialog {
  private readonly api = inject(LedgerApi);
  protected readonly debt = inject<Debt>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<void>>(DialogRef);

  protected readonly payments = rxResource({ stream: () => this.api.debtPayments(this.debt.id) });
}
