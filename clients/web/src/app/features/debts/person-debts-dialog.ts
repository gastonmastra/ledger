import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ContactDebts, Debt } from '../../core/api/models';
import { Button, DialogLayout, Icon, MoneyPipe, RowAction } from '../../ui';
import { amountClass } from '../shared/amount';

export interface PersonDebtsData {
  balance: ContactDebts;
  /** Their open debts (outside groups) in that currency. */
  debts: Debt[];
}

/** Closes with 'pay' to record a payment, or with a debt to open it. */
export type PersonDebtsResult = 'pay' | Debt;

/** What a person and I owe each other, debt by debt. */
@Component({
  selector: 'app-person-debts-dialog',
  imports: [DatePipe, DialogLayout, Button, Icon, MoneyPipe, RowAction],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [heading]="b.name">
      <div class="grid gap-4 text-sm">
        <dl class="grid grid-cols-3 gap-2 text-center">
          <div class="rounded-ui bg-surface-muted p-2">
            <dt class="text-xs text-muted" i18n="@@debts.owesMe">Owes me</dt>
            <dd class="font-medium tabular-nums">{{ b.owedToMe | money: b.currency }}</dd>
          </div>
          <div class="rounded-ui bg-surface-muted p-2">
            <dt class="text-xs text-muted" i18n="@@debts.iOwe">I owe</dt>
            <dd class="font-medium tabular-nums">{{ b.iOwe | money: b.currency }}</dd>
          </div>
          <div class="rounded-ui bg-surface-muted p-2">
            <dt class="text-xs text-muted" i18n="@@debts.net">Net</dt>
            <dd class="font-semibold tabular-nums" [class]="amountClass(b.net)">{{ b.net | money: b.currency: true }}</dd>
          </div>
        </dl>
        <ul class="divide-y divide-border rounded-ui border border-border">
          @for (debt of data.debts; track debt.id) {
            <li uiRowAction (rowOpen)="ref.close(debt)" class="flex items-center gap-3 px-3 py-2">
              <span class="min-w-0 flex-1">
                <span class="block truncate">{{ debt.description || '—' }}</span>
                <span class="text-xs text-muted">{{ debt.occurredAt | date: 'mediumDate' }}</span>
              </span>
              <span class="text-right tabular-nums" [class]="debt.direction === 'OwedToMe' ? 'text-success' : 'text-danger'">
                {{ debt.remaining | money: debt.currency }}
                @if (debt.isPartiallyPaid) {
                  <span class="block text-xs text-muted" i18n="@@debts.ofOriginal">of {{ debt.amount | money: debt.currency }}</span>
                }
              </span>
              <ui-icon name="chevronRight" size="sm" class="text-muted" />
            </li>
          }
        </ul>
      </div>
      <div dialog-actions class="contents">
        <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.close">Close</button>
        <button uiButton type="button" (click)="ref.close('pay')" i18n="@@debts.recordPaymentShort">Record payment</button>
      </div>
    </ui-dialog>
  `,
})
export class PersonDebtsDialog {
  protected readonly data = inject<PersonDebtsData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<PersonDebtsResult>>(DialogRef);
  protected readonly b = this.data.balance;
  protected readonly amountClass = amountClass;
}
