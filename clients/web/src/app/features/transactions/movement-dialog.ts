import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { Button, DialogLayout, FormField, Input, MoneyPipe } from '../../ui';

export type MovementKind = 'income' | 'expense';

/** Records money coming into (income) or going out of (expense) a wallet. */
@Component({
  selector: 'app-movement-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog [heading]="heading">
        <div class="grid gap-4">
          <ui-form-field i18n-label="@@movement.wallet" label="Wallet">
            <select uiInput formControlName="walletId">
              <option value="" disabled i18n="@@ui.choose">Choose…</option>
              @for (wallet of wallets.value(); track wallet.id) {
                <option [value]="wallet.id">{{ wallet.name }} · {{ wallet.balance | money: wallet.currency }}</option>
              }
            </select>
          </ui-form-field>
          <ui-form-field [label]="amountLabel()">
            <input uiInput type="number" min="0.01" step="0.01" formControlName="amount" />
          </ui-form-field>
          <ui-form-field i18n-label="@@movement.category" label="Category"
                         i18n-hint="@@movement.categoryHint" hint="Optional. Create categories in the Categories page.">
            <select uiInput formControlName="categoryId">
              <option value="" i18n="@@movement.uncategorized">Uncategorized</option>
              @for (category of categories(); track category.id) {
                <option [value]="category.id">{{ category.name }}</option>
              }
            </select>
          </ui-form-field>
          <ui-form-field i18n-label="@@movement.description" label="Description">
            <input uiInput formControlName="description" maxlength="500" />
          </ui-form-field>
        </div>
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton type="submit" [disabled]="form.invalid" [loading]="saving()" i18n="@@ui.save">Save</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class MovementDialog {
  private readonly api = inject(LedgerApi);
  protected readonly kind = inject<MovementKind>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly saving = signal(false);

  protected readonly heading =
    this.kind === 'income'
      ? $localize`:@@movement.incomeTitle:Record income`
      : $localize`:@@movement.expenseTitle:Record expense`;

  protected readonly wallets = rxResource({ stream: () => this.api.wallets() });
  private readonly allCategories = rxResource({ stream: () => this.api.categories() });

  protected readonly categories = computed(() =>
    (this.allCategories.value() ?? []).filter((c) => c.type === (this.kind === 'income' ? 'Income' : 'Expense')),
  );

  protected readonly form = inject(NonNullableFormBuilder).group({
    walletId: ['', Validators.required],
    amount: [null as number | null, [Validators.required, Validators.min(0.01)]],
    categoryId: [''],
    description: [''],
  });

  private readonly walletId = toSignal(this.form.controls.walletId.valueChanges, { initialValue: '' });

  protected readonly amountLabel = computed(() => {
    const currency = this.wallets.value()?.find((w) => w.id === this.walletId())?.currency;
    return currency ? $localize`:@@movement.amountIn:Amount (${currency}:currency:)` : $localize`:@@movement.amount:Amount`;
  });

  protected async save(): Promise<void> {
    if (this.form.invalid) return;

    const value = this.form.getRawValue();
    const body = {
      walletId: value.walletId,
      amount: value.amount!,
      categoryId: value.categoryId || null,
      description: value.description.trim() || null,
    };

    this.saving.set(true);

    try {
      await firstValueFrom(this.kind === 'income' ? this.api.recordIncome(body) : this.api.recordExpense(body));
      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}
