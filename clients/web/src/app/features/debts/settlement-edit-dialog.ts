import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { LedgerApi } from '../../core/api/ledger-api';
import { OperationDetails } from '../../core/api/models';
import { OperationTracker } from '../../core/operations/operation-tracker';
import { Button, DateTimeInput, DialogLayout, FormField, Input, MoneyPipe, ReasonField } from '../../ui';

/**
 * Changes the date, description or wallet of a group settlement. The amount is the group's suggested payment:
 * to pay a different amount, delete it and record the suggested payment again. (Payments outside groups use
 * PaymentDialog, where the amount can change.)
 */
@Component({
  selector: 'app-settlement-edit-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button, MoneyPipe, DateTimeInput, ReasonField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog i18n-heading="@@settlementEdit.title" heading="Change the settlement"
                 i18n-description="@@settlementEdit.groupDescription" description="The amount was the group's suggested payment. To pay a different amount, delete it and pay the new suggestion.">
        <div class="grid gap-4">
          <p class="text-sm">
            {{ operation.fromName }} → {{ operation.toName }} ·
            <span class="font-medium tabular-nums">{{ operation.total | money: operation.currency }}</span>
          </p>
          @if (asksWallet()) {
            <ui-form-field i18n-label="@@settlementEdit.wallet" label="Wallet">
              <select uiInput formControlName="walletId">
                <option value="" disabled i18n="@@ui.choose">Choose…</option>
                @for (wallet of wallets(); track wallet.id) {
                  <option [value]="wallet.id">{{ wallet.name }} · {{ wallet.balance | money: wallet.currency }}</option>
                }
              </select>
            </ui-form-field>
          }
          <ui-form-field i18n-label="@@movement.description" label="Description">
            <input uiInput formControlName="description" maxlength="500" />
          </ui-form-field>
          <ui-form-field i18n-label="@@ui.when" label="When">
            <ui-datetime-input formControlName="occurredAt" />
          </ui-form-field>
          <ui-reason-field formControlName="reason" />
        </div>
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton type="submit" [disabled]="form.invalid || (asksWallet() && !form.value.walletId)" [loading]="saving()" i18n="@@ui.save">Save</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class SettlementEditDialog {
  private readonly api = inject(LedgerApi);
  private readonly tracker = inject(OperationTracker);
  protected readonly operation = inject<OperationDetails>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly saving = signal(false);

  private readonly allWallets = rxResource({ stream: () => this.api.wallets() });
  protected readonly wallets = computed(() =>
    (this.allWallets.value() ?? []).filter((w) => w.currency === this.operation.currency),
  );

  /** Only whoever recorded it chose a wallet, and only if money moved. */
  protected readonly asksWallet = computed(() => this.operation.recordedByMe && this.operation.total > 0);

  protected readonly form = inject(NonNullableFormBuilder).group({
    walletId: [this.operation.walletId ?? ''],
    description: [this.operation.description ?? ''],
    occurredAt: [this.operation.occurredAt, Validators.required],
    reason: ['', [Validators.required, Validators.pattern(/\S/)]],
  });

  protected async save(): Promise<void> {
    if (this.form.invalid) return;

    const value = this.form.getRawValue();
    this.saving.set(true);

    try {
      await this.tracker.submitChange(
        this.api.reviseOperation(this.operation.id, {
          reason: value.reason.trim(),
          occurredAt: value.occurredAt,
          description: value.description.trim() || null,
          walletId: this.asksWallet() ? value.walletId : null,
        }),
        $localize`:@@change.applied:Change applied.`,
      );
      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}
