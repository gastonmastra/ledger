import { DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { Button, DialogLayout, FormField, Input, MoneyPipe } from '../../ui';

/** Moves money between two of my wallets; across currencies it asks for the amount received. */
@Component({
  selector: 'app-transfer-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog i18n-heading="@@transfer.title" heading="Transfer between wallets">
        <div class="grid gap-4">
          <div class="grid gap-4 sm:grid-cols-2">
            <ui-form-field i18n-label="@@transfer.from" label="From">
              <select uiInput formControlName="fromWalletId">
                <option value="" disabled i18n="@@ui.choose">Choose…</option>
                @for (wallet of wallets.value(); track wallet.id) {
                  <option [value]="wallet.id">{{ wallet.name }} · {{ wallet.balance | money: wallet.currency }}</option>
                }
              </select>
            </ui-form-field>
            <ui-form-field i18n-label="@@transfer.to" label="To">
              <select uiInput formControlName="toWalletId">
                <option value="" disabled i18n="@@ui.choose">Choose…</option>
                @for (wallet of wallets.value(); track wallet.id) {
                  @if (wallet.id !== fromId()) {
                    <option [value]="wallet.id">{{ wallet.name }} · {{ wallet.currency }}</option>
                  }
                }
              </select>
            </ui-form-field>
          </div>
          <div class="grid gap-4 sm:grid-cols-2">
            <ui-form-field i18n-label="@@transfer.amount" label="Amount sent">
              <input uiInput type="number" min="0.01" step="0.01" formControlName="amount" />
            </ui-form-field>
            @if (crossCurrency()) {
              <ui-form-field i18n-label="@@transfer.received" label="Amount received"
                             i18n-hint="@@transfer.receivedHint" hint="Different currencies: enter what arrived.">
                <input uiInput type="number" min="0.01" step="0.01" formControlName="receivedAmount" />
              </ui-form-field>
            }
          </div>
          <ui-form-field i18n-label="@@movement.description" label="Description">
            <input uiInput formControlName="description" maxlength="500" />
          </ui-form-field>
        </div>
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton type="submit" [disabled]="!canSave()" [loading]="saving()" i18n="@@transfer.submit">Transfer</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class TransferDialog {
  private readonly api = inject(LedgerApi);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly saving = signal(false);
  protected readonly wallets = rxResource({ stream: () => this.api.wallets() });

  protected readonly form = inject(NonNullableFormBuilder).group({
    fromWalletId: ['', Validators.required],
    toWalletId: ['', Validators.required],
    amount: [null as number | null, [Validators.required, Validators.min(0.01)]],
    receivedAmount: [null as number | null],
    description: [''],
  });

  private readonly value = toSignal(this.form.valueChanges, { initialValue: this.form.value });
  protected readonly fromId = computed(() => this.value().fromWalletId);

  protected readonly crossCurrency = computed(() => {
    const wallets = this.wallets.value() ?? [];
    const from = wallets.find((w) => w.id === this.value().fromWalletId);
    const to = wallets.find((w) => w.id === this.value().toWalletId);
    return !!from && !!to && from.currency !== to.currency;
  });

  protected readonly canSave = computed(() => {
    const value = this.value();
    return (
      !!value.fromWalletId &&
      !!value.toWalletId &&
      value.fromWalletId !== value.toWalletId &&
      (value.amount ?? 0) > 0 &&
      (!this.crossCurrency() || (value.receivedAmount ?? 0) > 0)
    );
  });

  protected async save(): Promise<void> {
    if (!this.canSave()) return;

    const value = this.form.getRawValue();
    this.saving.set(true);

    try {
      await firstValueFrom(
        this.api.transfer({
          fromWalletId: value.fromWalletId,
          toWalletId: value.toWalletId,
          amount: value.amount!,
          receivedAmount: this.crossCurrency() ? value.receivedAmount : null,
          description: value.description.trim() || null,
        }),
      );
      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}
