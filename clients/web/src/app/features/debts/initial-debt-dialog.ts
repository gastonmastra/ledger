import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { LedgerApi } from '../../core/api/ledger-api';
import { CURRENCIES, Currency, InitialDebtDirection, OperationDetails } from '../../core/api/models';
import { OperationTracker } from '../../core/operations/operation-tracker';
import { Button, DateTimeInput, DialogLayout, FormField, Input, ReasonField, nowIso } from '../../ui';

/**
 * A debt that existed before using Ledger ("I already owed Sofi 50"). It is neither income nor
 * expense (like a wallet's opening balance) and can then be settled like any other debt.
 * Opened with an existing debt, it records a new version (linked users confirm it).
 */
@Component({
  selector: 'app-initial-debt-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button, DateTimeInput, ReasonField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog [heading]="heading"
                 i18n-description="@@initialDebt.description" description="A debt from before you used Ledger. No money moves now; settle it when it is paid. It is neither income nor expense.">
        <div class="grid gap-4">
          @if (mine) {
            <div class="inline-flex w-full rounded-ui border border-border p-1" role="radiogroup">
              @for (option of directions; track option.value) {
                <button type="button" role="radio" [attr.aria-checked]="direction() === option.value"
                        class="flex-1 rounded-ui px-3 py-1.5 text-sm cursor-pointer"
                        [class]="direction() === option.value ? 'bg-primary text-primary-foreground' : 'text-muted hover:text-foreground'"
                        (click)="form.controls.direction.setValue(option.value)">
                  {{ option.label }}
                </button>
              }
            </div>
            <ui-form-field i18n-label="@@initialDebt.contact" label="Contact">
              <select uiInput formControlName="contactId">
                <option value="" disabled i18n="@@ui.choose">Choose…</option>
                @for (contact of contacts.value(); track contact.id) {
                  <option [value]="contact.id">{{ contact.name }}</option>
                }
              </select>
            </ui-form-field>
          } @else {
            <p class="text-sm text-muted" i18n="@@initialDebt.recordedBy">Recorded by {{ edit!.recordedByName // i18n(ph="name") }}. Only they can change who owes whom or the currency.</p>
          }
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
          <ui-form-field i18n-label="@@movement.description" label="Description">
            <input uiInput formControlName="description" maxlength="500" />
          </ui-form-field>
          <ui-form-field i18n-label="@@initialDebt.when" label="When did it start?">
            <ui-datetime-input formControlName="occurredAt" />
          </ui-form-field>
          @if (edit) {
            <ui-reason-field formControlName="reason" />
          }
        </div>
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton type="submit" [disabled]="form.invalid" [loading]="saving()" i18n="@@ui.save">Save</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class InitialDebtDialog {
  private readonly api = inject(LedgerApi);
  private readonly tracker = inject(OperationTracker);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly edit = inject<OperationDetails | null>(DIALOG_DATA, { optional: true }) ?? null;
  protected readonly mine = !this.edit || this.edit.recordedByMe;
  protected readonly saving = signal(false);
  protected readonly currencies = CURRENCIES;
  protected readonly heading = this.edit
    ? $localize`:@@initialDebt.editTitle:Change the existing debt`
    : $localize`:@@initialDebt.title:Add an existing debt`;

  protected readonly directions: { value: InitialDebtDirection; label: string }[] = [
    { value: 'IOwe', label: $localize`:@@initialDebt.iOwe:I owed them` },
    { value: 'TheyOweMe', label: $localize`:@@initialDebt.theyOweMe:They owed me` },
  ];

  protected readonly contacts = rxResource({ stream: () => this.api.contacts() });

  protected readonly form = inject(NonNullableFormBuilder).group({
    direction: [this.edit?.direction ?? ('IOwe' as InitialDebtDirection), Validators.required],
    contactId: [this.edit?.contactId ?? '', this.mine ? Validators.required : []],
    amount: [this.edit?.total ?? (null as number | null), [Validators.required, Validators.min(0.01)]],
    currency: [this.edit?.currency ?? ('EUR' as Currency), Validators.required],
    description: [this.edit?.description ?? ''],
    occurredAt: [this.edit?.occurredAt ?? nowIso(), Validators.required],
    reason: ['', this.edit ? [Validators.required, Validators.pattern(/\S/)] : []],
  });

  constructor() {
    // The currency belongs to whoever recorded it.
    if (!this.mine) this.form.controls.currency.disable();
  }

  protected readonly direction = toSignal(this.form.controls.direction.valueChanges, {
    initialValue: this.form.controls.direction.value,
  });

  protected async save(): Promise<void> {
    if (this.form.invalid) return;

    const value = this.form.getRawValue();
    this.saving.set(true);

    try {
      if (this.edit) {
        await this.tracker.submitChange(
          this.api.reviseOperation(this.edit.id, {
            reason: value.reason.trim(),
            occurredAt: value.occurredAt,
            description: value.description.trim() || null,
            amount: value.amount!,
            currency: value.currency,
            contactId: this.mine ? value.contactId : null,
            direction: this.mine ? value.direction : null,
          }),
          $localize`:@@change.applied:Change applied.`,
        );
      } else {
        await this.tracker.submit(
          this.api.recordInitialDebt({
            contactId: value.contactId,
            direction: value.direction,
            amount: value.amount!,
            currency: value.currency,
            description: value.description.trim() || null,
            occurredAt: value.occurredAt,
          }),
          $localize`:@@initialDebt.saved:Existing debt recorded.`,
        );
      }
      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}
