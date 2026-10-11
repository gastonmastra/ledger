import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { of } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { Debt } from '../../core/api/models';
import { debtStatusLabel } from '../../core/i18n/labels';
import { Badge, Button, DialogLayout, EmptyState, Icon, MoneyPipe, Spinner } from '../../ui';

/** Closes with 'operation' to open the loan, split or existing debt that created it. */
export type DebtAction = 'operation';

/** A debt: what it was, what was paid (and by which payments) and what is left. */
@Component({
  selector: 'app-debt-detail-dialog',
  imports: [DatePipe, DialogLayout, Badge, Button, EmptyState, Icon, MoneyPipe, Spinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [heading]="debt.description ?? debt.counterpartyName" [description]="who">
      <div class="grid gap-4 text-sm">
        <div class="flex flex-wrap items-center gap-2">
          <ui-badge [tone]="debt.status === 'Open' && !debt.isSettling ? 'primary' : 'neutral'">{{ statusLabel(debt.status, debt.isSettling) }}</ui-badge>
          @if (debt.isPartiallyPaid) {
            <ui-badge tone="warning" i18n="@@debts.partiallyPaid">Partially paid</ui-badge>
          }
          @if (debt.groupName) {
            <ui-badge>{{ debt.groupName }}</ui-badge>
          }
          <span class="text-muted">{{ debt.occurredAt | date: 'mediumDate' }}</span>
        </div>
        <dl class="grid grid-cols-3 gap-2 text-center">
          <div class="rounded-ui bg-surface-muted p-2">
            <dt class="text-xs text-muted" i18n="@@debtDetail.amount">Amount</dt>
            <dd class="font-medium tabular-nums">{{ debt.amount | money: debt.currency }}</dd>
          </div>
          <div class="rounded-ui bg-surface-muted p-2">
            <dt class="text-xs text-muted" i18n="@@debtDetail.paid">Paid</dt>
            <dd class="font-medium tabular-nums">{{ debt.paid | money: debt.currency }}</dd>
          </div>
          <div class="rounded-ui bg-surface-muted p-2">
            <dt class="text-xs text-muted" i18n="@@debtDetail.left">Left</dt>
            <dd class="font-semibold tabular-nums">{{ debt.remaining | money: debt.currency }}</dd>
          </div>
        </dl>
        @if (!debt.groupId) {
          <section>
            <h3 class="mb-2 font-semibold" i18n="@@debts.payments">Payments</h3>
            @if (payments.isLoading() && !payments.value()) {
              <div class="flex justify-center py-2"><ui-spinner /></div>
            } @else if (!payments.value()?.length) {
              <ui-empty-state i18n-message="@@debtPayments.none" message="Nothing paid yet." />
            } @else {
              <ul class="grid gap-1">
                @for (payment of payments.value(); track payment.settlementOperationId) {
                  <li class="flex items-center gap-2">
                    <span class="flex-1">{{ payment.description }} <span class="text-xs text-muted">· {{ payment.occurredAt | date: 'mediumDate' }}</span></span>
                    @if (payment.status === 'Pending') {
                      <ui-badge tone="warning" i18n="@@debtStatus.settling">Settling</ui-badge>
                    }
                    <span class="tabular-nums">{{ payment.amount | money: debt.currency }}</span>
                  </li>
                }
              </ul>
            }
          </section>
        }
      </div>
      <div dialog-actions class="contents">
        <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.close">Close</button>
        <button uiButton type="button" (click)="ref.close('operation')">
          <ui-icon name="scale" size="sm" /><ng-container i18n="@@debtDetail.operation">What created it</ng-container>
        </button>
      </div>
    </ui-dialog>
  `,
})
export class DebtDetailDialog {
  private readonly api = inject(LedgerApi);
  protected readonly debt = inject<Debt>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<DebtAction>>(DialogRef);
  protected readonly statusLabel = debtStatusLabel;

  protected readonly who = this.debt.direction === 'OwedToMe'
    ? $localize`:@@debtDetail.owesYou:${this.debt.counterpartyName}:name: owes you`
    : $localize`:@@debtDetail.youOwe:You owe ${this.debt.counterpartyName}:name:`;

  protected readonly payments = rxResource({
    stream: () => (this.debt.groupId || !this.debt.paid ? of([]) : this.api.debtPayments(this.debt.id)),
  });
}
