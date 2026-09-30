import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { LedgerApi } from '../../core/api/ledger-api';
import { CURRENCIES, Currency, OperationDetails } from '../../core/api/models';
import { CurrentUser } from '../../core/current-user';
import { OperationTracker } from '../../core/operations/operation-tracker';
import { Button, DateTimeInput, DialogLayout, FormField, Input, MoneyPipe, ReasonField, nowIso } from '../../ui';

/**
 * Lend money from one of my wallets to a contact. Opened with a loan, it records a new version
 * (linked users confirm it); only whoever lent the money can change the contact or the wallet.
 */
@Component({
  selector: 'app-lend-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button, MoneyPipe, DateTimeInput, ReasonField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog [heading]="heading">
        <div class="grid gap-4">
          @if (mine) {
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
          } @else {
            <p class="text-sm text-muted" i18n="@@lend.recordedBy">{{ edit!.recordedByName // i18n(ph="name") }} lent it to {{ edit!.toName // i18n(ph="borrower") }}. Only they can change who got it or the wallet.</p>
          }
          <ui-form-field i18n-label="@@movement.amount" label="Amount">
            <input uiInput type="number" min="0.01" step="0.01" formControlName="amount" />
          </ui-form-field>
          <ui-form-field i18n-label="@@movement.description" label="Description">
            <input uiInput formControlName="description" maxlength="500" />
          </ui-form-field>
          <ui-form-field i18n-label="@@ui.when" label="When">
            <ui-datetime-input formControlName="occurredAt" />
          </ui-form-field>
          @if (edit) {
            <ui-reason-field formControlName="reason" />
          }
        </div>
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton type="submit" [disabled]="form.invalid" [loading]="saving()">{{ submitLabel }}</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class LendDialog {
  private readonly api = inject(LedgerApi);
  private readonly tracker = inject(OperationTracker);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly edit = inject<OperationDetails | null>(DIALOG_DATA, { optional: true }) ?? null;
  protected readonly mine = !this.edit || this.edit.recordedByMe;
  protected readonly saving = signal(false);
  protected readonly contacts = rxResource({ stream: () => this.api.contacts() });
  protected readonly wallets = rxResource({ stream: () => this.api.wallets() });

  protected readonly heading = this.edit ? $localize`:@@lend.editTitle:Change the loan` : $localize`:@@lend.title:Lend money`;
  protected readonly submitLabel = this.edit ? $localize`:@@ui.save:Save` : $localize`:@@lend.submit:Lend`;

  protected readonly form = inject(NonNullableFormBuilder).group({
    contactId: [this.edit?.contactId ?? '', this.mine ? Validators.required : []],
    walletId: [this.edit?.walletId ?? '', this.mine ? Validators.required : []],
    amount: [this.edit?.total ?? (null as number | null), [Validators.required, Validators.min(0.01)]],
    description: [this.edit?.description ?? ''],
    occurredAt: [this.edit?.occurredAt ?? nowIso(), Validators.required],
    reason: ['', this.edit ? [Validators.required, Validators.pattern(/\S/)] : []],
  });

  protected async save(): Promise<void> {
    if (this.form.invalid) return;

    const value = this.form.getRawValue();
    const name = this.contacts.value()?.find((c) => c.id === value.contactId)?.name ?? '';
    this.saving.set(true);

    try {
      if (this.edit) {
        await this.tracker.submitChange(
          this.api.reviseOperation(this.edit.id, {
            reason: value.reason.trim(),
            occurredAt: value.occurredAt,
            description: value.description.trim() || null,
            amount: value.amount!,
            contactId: this.mine ? value.contactId : null,
            walletId: this.mine ? value.walletId : null,
          }),
          $localize`:@@change.applied:Change applied.`,
        );
        this.ref.close(true);
        return;
      }

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

