import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { OperationDetails } from '../../core/api/models';
import { operationKindLabel, operationStatusLabel } from '../../core/i18n/labels';
import { Badge, Button, DialogLayout, Icon, MoneyPipe } from '../../ui';

/** What the user chose in the detail: OperationActions runs it. */
export type OperationAction = 'edit' | 'delete' | 'undoPayment' | 'restore' | 'history';

/** A loan, existing debt, split or payment: who paid, who owes, what a payment paid, and what can be done. */
@Component({
  selector: 'app-operation-detail-dialog',
  imports: [DatePipe, DialogLayout, Badge, Button, Icon, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [heading]="o.description || kindLabel(o.kind)">
      <div class="grid gap-4 text-sm">
        <div class="flex flex-wrap items-center gap-2">
          <ui-badge tone="primary">{{ kindLabel(o.kind) }}</ui-badge>
          @if (o.groupName) {
            <ui-badge>{{ o.groupName }}</ui-badge>
          }
          @if (o.status !== 'Completed') {
            <ui-badge [tone]="o.status === 'Deleted' || o.status === 'Rejected' ? 'danger' : 'neutral'">{{ statusLabel(o.status) }}</ui-badge>
          }
          @if (o.isDisputed) {
            <ui-badge tone="danger" i18n="@@operations.disputed">Disputed</ui-badge>
          }
          @if (o.blockedReason === 'debt.settled') {
            <ui-badge i18n="@@operations.hasPayments">Has payments</ui-badge>
          }
        </div>
        <div class="flex items-baseline justify-between gap-4">
          <span class="text-muted">{{ o.occurredAt | date: 'medium' }}</span>
          <span class="text-2xl font-semibold tabular-nums">{{ o.total | money: o.currency }}</span>
        </div>
        @if (o.kind !== 'Split') {
          <p>{{ o.fromName }} → {{ o.toName }}</p>
        }
        @if (!o.recordedByMe) {
          <p class="text-muted" i18n="@@operations.recordedBy">recorded by {{ o.recordedByName // i18n(ph="name") }}</p>
        }
        @if (o.pendingChange; as change) {
          <p class="rounded-ui bg-warning-soft p-2 text-warning" i18n="@@operations.pendingDetail">A change asked by {{ change.requestedByName // i18n(ph="name") }} is waiting for {{ change.waitingFor.join(', ') // i18n(ph="people") }}: “{{ change.reason // i18n(ph="reason") }}”</p>
        }
        @if (o.payers.length && o.kind === 'Split') {
          <section>
            <h3 class="mb-1 font-semibold" i18n="@@operations.paidBy">Paid by</h3>
            @for (payer of o.payers; track payer.personId) {
              <p class="flex justify-between"><span>{{ payer.name }}</span><span class="tabular-nums">{{ payer.amount | money: o.currency }}</span></p>
            }
          </section>
        }
        @if (o.shares.length) {
          <section>
            <h3 class="mb-1 font-semibold" i18n="@@operations.sharedBy">Shared by</h3>
            @for (share of o.shares; track share.personId) {
              <p class="flex justify-between"><span>{{ share.name }}</span><span class="tabular-nums">{{ share.amount | money: o.currency }}</span></p>
            }
          </section>
        }
        @if (o.allocations?.length || o.extraAmount) {
          <section>
            <h3 class="mb-1 font-semibold" i18n="@@payment.preview">What it pays</h3>
            @for (allocation of o.allocations; track allocation.debtId) {
              <p class="flex justify-between gap-4">
                <span>{{ allocation.description || '—' }} <span class="text-xs text-muted">· {{ allocation.occurredAt | date: 'mediumDate' }}</span></span>
                <span class="tabular-nums">{{ allocation.amount | money: o.currency }}</span>
              </p>
            }
            @if (o.extraAmount) {
              <p class="text-muted" i18n="@@history.extraDebt">{{ o.extraAmount | money: o.currency // i18n(ph="amount") }} beyond what was owed became a new debt</p>
            }
          </section>
        }
        @if (o.rejectionReason) {
          <p class="text-danger">{{ o.rejectionReason }}</p>
        }
      </div>
      <div dialog-actions class="contents">
        <button uiButton variant="ghost" type="button" (click)="ref.close('history')">
          <ui-icon name="clock" size="sm" /><ng-container i18n="@@history.title">History</ng-container>
        </button>
        @if (o.blockedReason === 'debt.settled' && o.status === 'Completed') {
          <button uiButton variant="secondary" type="button" (click)="ref.close('undoPayment')"
                  i18n-title="@@operations.undoPaymentHint" title="It was paid (fully or partly): undo the payment to change it.">
            <ui-icon name="undo" size="sm" /><ng-container i18n="@@operations.undoPayment">Undo payment</ng-container>
          </button>
        }
        @if (o.canRestore) {
          <button uiButton variant="secondary" type="button" (click)="ref.close('restore')">
            <ui-icon name="undo" size="sm" /><ng-container i18n="@@history.restore">Restore</ng-container>
          </button>
        }
        @if (o.canDelete) {
          <button uiButton variant="secondary" type="button" (click)="ref.close('delete')">
            <ui-icon name="trash" size="sm" /><ng-container i18n="@@ui.delete">Delete</ng-container>
          </button>
        }
        @if (o.canEdit) {
          <button uiButton type="button" (click)="ref.close('edit')">
            <ui-icon name="pencil" size="sm" /><ng-container i18n="@@ui.edit">Edit</ng-container>
          </button>
        }
      </div>
    </ui-dialog>
  `,
})
export class OperationDetailDialog {
  protected readonly o = inject<OperationDetails>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<OperationAction>>(DialogRef);
  protected readonly kindLabel = operationKindLabel;
  protected readonly statusLabel = operationStatusLabel;
}
