import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { Wallet } from '../../core/api/models';
import { Button, DateTimeInput, DialogLayout, FormField, Input, MoneyPipe, Spinner, nowIso } from '../../ui';

/** Replaces the opening balance of a wallet (amount and date). */
@Component({
  selector: 'app-opening-balance-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button, DateTimeInput, Spinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog i18n-heading="@@opening.title" heading="Opening balance"
                 i18n-description="@@opening.description" description="What the wallet had when you started using Ledger. The previous value is kept in the history as replaced.">
        @if (loading()) {
          <div class="flex justify-center py-6"><ui-spinner /></div>
        } @else {
          <div class="grid gap-4">
            <ui-form-field [label]="amountLabel">
              <input uiInput type="number" min="0" step="0.01" formControlName="amount" />
            </ui-form-field>
            <ui-form-field i18n-label="@@wallets.openingDate" label="Date of the opening balance">
              <ui-datetime-input formControlName="occurredAt" />
            </ui-form-field>
          </div>
        }
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton type="submit" [disabled]="form.invalid || loading()" [loading]="saving()" i18n="@@ui.save">Save</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class OpeningBalanceDialog {
  private readonly api = inject(LedgerApi);
  protected readonly wallet = inject<Wallet>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly amountLabel = $localize`:@@opening.amount:Amount (${this.wallet.currency}:currency:)`;

  protected readonly form = inject(NonNullableFormBuilder).group({
    amount: [0, [Validators.required, Validators.min(0)]],
    occurredAt: [nowIso(), Validators.required],
  });

  constructor() {
    firstValueFrom(this.api.openingBalance(this.wallet.id))
      .then((opening) =>
        this.form.setValue({ amount: opening.amount, occurredAt: opening.occurredAt ?? this.wallet.createdAt }),
      )
      .catch(() => this.ref.close())
      .finally(() => this.loading.set(false));
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) return;

    const { amount, occurredAt } = this.form.getRawValue();
    this.saving.set(true);

    try {
      await firstValueFrom(this.api.changeOpeningBalance(this.wallet.id, amount ?? 0, occurredAt));
      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}

/** Sets a wallet to its real balance; the difference is recorded as a balance adjustment. */
@Component({
  selector: 'app-adjust-balance-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button, DateTimeInput, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog i18n-heading="@@adjust.title" heading="Adjust balance"
                 i18n-description="@@adjust.description" description="Enter what the wallet really has (e.g. your bank balance). The difference is recorded as a balance adjustment.">
        <div class="grid gap-4">
          <p class="text-sm text-muted">
            <span i18n="@@adjust.current">Balance in Ledger:</span>
            <span class="font-medium text-foreground tabular-nums"> {{ wallet.balance | money: wallet.currency }}</span>
          </p>
          <ui-form-field i18n-label="@@adjust.actual" label="Real balance">
            <input uiInput type="number" min="0" step="0.01" formControlName="actualBalance" />
          </ui-form-field>
          <ui-form-field i18n-label="@@movement.description" label="Description">
            <input uiInput formControlName="description" maxlength="500" />
          </ui-form-field>
          <ui-form-field i18n-label="@@ui.when" label="When">
            <ui-datetime-input formControlName="occurredAt" />
          </ui-form-field>
        </div>
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton type="submit" [disabled]="form.invalid" [loading]="saving()" i18n="@@adjust.submit">Adjust</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class AdjustBalanceDialog {
  private readonly api = inject(LedgerApi);
  protected readonly wallet = inject<Wallet>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly saving = signal(false);

  protected readonly form = inject(NonNullableFormBuilder).group({
    actualBalance: [this.wallet.balance as number | null, [Validators.required, Validators.min(0)]],
    description: [''],
    occurredAt: [nowIso(), Validators.required],
  });

  protected async save(): Promise<void> {
    if (this.form.invalid) return;

    const value = this.form.getRawValue();
    this.saving.set(true);

    try {
      await firstValueFrom(
        this.api.adjustBalance({
          walletId: this.wallet.id,
          actualBalance: value.actualBalance!,
          occurredAt: value.occurredAt,
          description: value.description.trim() || null,
        }),
      );
      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}
