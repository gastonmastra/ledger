import { DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { LedgerApi } from '../../core/api/ledger-api';
import { CURRENCIES, Currency } from '../../core/api/models';
import { CurrentUser } from '../../core/current-user';
import { OperationTracker } from '../../core/operations/operation-tracker';
import { Button, DateTimeInput, DialogLayout, FormField, Input, MoneyPipe, nowIso } from '../../ui';

/** Lend money from one of my wallets to a contact. */
@Component({
  selector: 'app-lend-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button, MoneyPipe, DateTimeInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog i18n-heading="@@lend.title" heading="Lend money">
        <div class="grid gap-4">
          <ui-form-field i18n-label="@@lend.to" label="To">
            <select uiInput formControlName="contactId">
              <option value="" disabled i18n="@@ui.choose">Choose…</option>
              @for (contact of contacts.value(); track contact.id) {
                <option [value]="contact.id">{{ contact.name }}</option>
              }
            </select>
          </ui-form-field>
          <ui-form-field i18n-label="@@lend.from" label="From wallet">
            <select uiInput formControlName="walletId">
              <option value="" disabled i18n="@@ui.choose">Choose…</option>
              @for (wallet of wallets.value(); track wallet.id) {
                <option [value]="wallet.id">{{ wallet.name }} · {{ wallet.balance | money: wallet.currency }}</option>
              }
            </select>
          </ui-form-field>
          <ui-form-field i18n-label="@@movement.amount" label="Amount">
            <input uiInput type="number" min="0.01" step="0.01" formControlName="amount" />
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
          <button uiButton type="submit" [disabled]="form.invalid" [loading]="saving()" i18n="@@lend.submit">Lend</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class LendDialog {
  private readonly api = inject(LedgerApi);
  private readonly tracker = inject(OperationTracker);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly saving = signal(false);
  protected readonly contacts = rxResource({ stream: () => this.api.contacts() });
  protected readonly wallets = rxResource({ stream: () => this.api.wallets() });

  protected readonly form = inject(NonNullableFormBuilder).group({
    contactId: ['', Validators.required],
    walletId: ['', Validators.required],
    amount: [null as number | null, [Validators.required, Validators.min(0.01)]],
    description: [''],
    occurredAt: [nowIso(), Validators.required],
  });

  protected async save(): Promise<void> {
    if (this.form.invalid) return;

    const value = this.form.getRawValue();
    const name = this.contacts.value()?.find((c) => c.id === value.contactId)?.name ?? '';
    this.saving.set(true);

    try {
      await this.tracker.submit(
        this.api.lend({
          contactId: value.contactId,
          walletId: value.walletId,
          amount: value.amount!,
          description: value.description.trim() || null,
          occurredAt: value.occurredAt,
        }),
        $localize`:@@lend.saved:Loan to ${name}:name: recorded.`,
      );
      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}

/** A friend paid something for me: I owe them the amount. */
@Component({
  selector: 'app-friend-paid-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button, DateTimeInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog i18n-heading="@@friendPaid.title" heading="A friend paid for me">
        <div class="grid gap-4">
          <ui-form-field i18n-label="@@friendPaid.who" label="Who paid?">
            <select uiInput formControlName="contactId">
              <option value="" disabled i18n="@@ui.choose">Choose…</option>
              @for (contact of contacts.value(); track contact.id) {
                <option [value]="contact.personId">{{ contact.name }}</option>
              }
            </select>
          </ui-form-field>
          <ui-form-field i18n-label="@@split.what" label="What was it?">
            <input uiInput formControlName="description" maxlength="500" />
          </ui-form-field>
          <div class="grid grid-cols-2 gap-4">
            <ui-form-field i18n-label="@@movement.amount" label="Amount">
              <input uiInput type="number" min="0.01" step="0.01" formControlName="amount" />
            </ui-form-field>
            <ui-form-field i18n-label="@@split.currency" label="Currency">
              <select uiInput formControlName="currency">
                @for (currency of currencies; track currency) {
                  <option [value]="currency">{{ currency }}</option>
                }
              </select>
            </ui-form-field>
          </div>
          <ui-form-field i18n-label="@@movement.category" label="Category">
            <select uiInput formControlName="categoryId">
              <option value="" i18n="@@movement.uncategorized">Uncategorized</option>
              @for (category of categories(); track category.id) {
                <option [value]="category.id">{{ category.name }}</option>
              }
            </select>
          </ui-form-field>
          <ui-form-field i18n-label="@@ui.when" label="When">
            <ui-datetime-input formControlName="occurredAt" />
          </ui-form-field>
        </div>
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton type="submit" [disabled]="form.invalid || !me()" [loading]="saving()" i18n="@@ui.save">Save</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class FriendPaidDialog {
  private readonly api = inject(LedgerApi);
  private readonly tracker = inject(OperationTracker);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly saving = signal(false);
  protected readonly currencies = CURRENCIES;
  protected readonly me = inject(CurrentUser).me.value;
  protected readonly contacts = rxResource({ stream: () => this.api.contacts() });
  private readonly allCategories = rxResource({ stream: () => this.api.categories() });
  protected readonly categories = computed(() => (this.allCategories.value() ?? []).filter((c) => c.type === 'Expense'));

  protected readonly form = inject(NonNullableFormBuilder).group({
    contactId: ['', Validators.required],
    description: ['', Validators.required],
    amount: [null as number | null, [Validators.required, Validators.min(0.01)]],
    currency: ['EUR' as Currency, Validators.required],
    categoryId: [''],
    occurredAt: [nowIso(), Validators.required],
  });

  protected async save(): Promise<void> {
    const me = this.me();
    if (this.form.invalid || !me) return;

    const value = this.form.getRawValue();
    this.saving.set(true);

    try {
      await this.tracker.submit(
        this.api.split({
          total: value.amount!,
          currency: value.currency,
          method: 'Custom',
          description: value.description.trim(),
          categoryId: value.categoryId || null,
          payers: [{ personId: value.contactId, amount: value.amount! }],
          participants: [{ personId: me.personId, value: value.amount! }],
          occurredAt: value.occurredAt,
        }),
        $localize`:@@friendPaid.saved:Recorded: you owe it to your friend.`,
      );
      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}

