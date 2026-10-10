import { DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { CURRENCIES, Currency } from '../../core/api/models';
import { Button, DateTimeInput, DialogLayout, FormField, Input, nowIso } from '../../ui';

@Component({
  selector: 'app-wallet-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button, DateTimeInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog i18n-heading="@@wallets.new" heading="New wallet"
                 i18n-description="@@wallets.newDescription" description="A bank account, cash, a card… Each wallet holds one currency.">
        <div class="grid gap-4">
          <ui-form-field i18n-label="@@wallets.name" label="Name">
            <input uiInput formControlName="name" maxlength="100" i18n-placeholder="@@wallets.namePlaceholder" placeholder="e.g. Bank account" />
          </ui-form-field>
          <div class="grid grid-cols-2 gap-4">
            <ui-form-field i18n-label="@@wallets.currency" label="Currency">
              <select uiInput formControlName="currency">
                @for (currency of currencies; track currency) {
                  <option [value]="currency">{{ currency }}</option>
                }
              </select>
            </ui-form-field>
            <ui-form-field i18n-label="@@wallets.openingBalance" label="Opening balance">
              <input uiInput type="number" min="0" step="0.01" formControlName="openingBalance" />
            </ui-form-field>
          </div>
          @if (form.controls.openingBalance.value) {
            <ui-form-field i18n-label="@@wallets.openingDate" label="Date of the opening balance">
              <ui-datetime-input formControlName="openingBalanceAt" />
            </ui-form-field>
          }
        </div>
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton type="submit" [disabled]="form.invalid" [loading]="saving()" i18n="@@ui.create">Create</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class WalletDialog {
  private readonly api = inject(LedgerApi);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly currencies = CURRENCIES;
  protected readonly saving = signal(false);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    currency: ['EUR' as Currency, Validators.required],
    openingBalance: [0, [Validators.required, Validators.min(0)]],
    openingBalanceAt: [nowIso()],
  });

  protected async save(): Promise<void> {
    if (this.form.invalid) return;

    const { name, currency, openingBalance, openingBalanceAt } = this.form.getRawValue();
    this.saving.set(true);

    try {
      await firstValueFrom(this.api.createWallet(name.trim(), currency, openingBalance ?? 0, openingBalanceAt || null));
      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}
