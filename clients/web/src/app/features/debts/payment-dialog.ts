import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { debounceTime, map, of } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import {
  CURRENCIES,
  Currency,
  OperationDetails,
  PaymentDirection,
  PaymentPreview,
  PreviewPaymentRequest,
} from '../../core/api/models';
import { OperationTracker } from '../../core/operations/operation-tracker';
import { Badge, Button, DateTimeInput, DialogLayout, FormField, Input, MoneyPipe, ReasonField, Spinner, nowIso } from '../../ui';
import { amountClass } from '../shared/amount';

/** A new payment with a contact (pre-filled from their balance), or an edit of a recorded payment. */
export type PaymentDialogData =
  | { contactId?: string; currency?: Currency; direction?: PaymentDirection; amount?: number }
  | { edit: OperationDetails };

/**
 * Records money paid between me and a contact, or changes a payment. It pays our debts outside groups from the
 * oldest: mutual debts cancel out first and anything paid beyond what was owed becomes a debt the other way.
 * A live preview shows which debts it pays and what is left.
 */
@Component({
  selector: 'app-payment-dialog',
  imports: [ReactiveFormsModule, DatePipe, DialogLayout, FormField, Input, Button, Badge, Spinner, MoneyPipe, DateTimeInput, ReasonField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog [heading]="heading"
                 i18n-description="@@payment.description"
                 description="It pays your debts with that person from the oldest. What you owe each other cancels out first.">
        <div class="grid gap-4">
          @if (edit) {
            <p class="text-sm">{{ edit.fromName }} → {{ edit.toName }}</p>
            @if (!edit.recordedByMe) {
              <p class="text-sm text-muted" i18n="@@payment.recordedBy">{{ edit.recordedByName // i18n(ph="name") }} recorded it. Only they can change the amount or the wallet.</p>
            }
          } @else {
            <div class="grid gap-4 sm:grid-cols-2">
              <ui-form-field i18n-label="@@payment.with" label="With">
                <select uiInput formControlName="contactId">
                  <option value="" disabled i18n="@@ui.choose">Choose…</option>
                  @for (contact of contacts.value(); track contact.id) {
                    <option [value]="contact.id">{{ contact.name }}</option>
                  }
                </select>
              </ui-form-field>
              <ui-form-field i18n-label="@@payment.currency" label="Currency">
                <select uiInput formControlName="currency">
                  @for (currency of currencies; track currency) {
                    <option [value]="currency">{{ currency }}</option>
                  }
                </select>
              </ui-form-field>
            </div>
            <ui-form-field i18n-label="@@payment.who" label="Who paid">
              <select uiInput formControlName="direction">
                <option value="TheyPaidMe" i18n="@@payment.theyPaidMe">{{ contactName() // i18n(ph="name") }} paid me</option>
                <option value="IPaidThem" i18n="@@payment.iPaidThem">I paid {{ contactName() // i18n(ph="name") }}</option>
              </select>
            </ui-form-field>
          }
          <div class="grid gap-4 sm:grid-cols-2">
            <ui-form-field i18n-label="@@movement.amount" label="Amount">
              <input uiInput type="number" min="0.01" step="0.01" formControlName="amount" />
            </ui-form-field>
            @if (asksWallet) {
              <ui-form-field [label]="walletLabel()">
                <select uiInput formControlName="walletId">
                  <option value="" disabled i18n="@@ui.choose">Choose…</option>
                  @for (wallet of wallets(); track wallet.id) {
                    <option [value]="wallet.id">{{ wallet.name }} · {{ wallet.balance | money: wallet.currency }}</option>
                  }
                </select>
              </ui-form-field>
            }
          </div>

          <section class="rounded-ui border border-border p-3 text-sm" aria-live="polite">
            <h3 class="mb-2 font-semibold" i18n="@@payment.preview">What it pays</h3>
            @if (preview.isLoading() && !preview.value()) {
              <div class="flex justify-center py-2"><ui-spinner /></div>
            } @else if (preview.value(); as p) {
              @if (!p.lines.length && !p.extra) {
                <p class="text-muted" i18n="@@payment.nothingToPay">Enter an amount.</p>
              }
              <ul class="grid gap-1">
                @for (line of p.lines; track line.debtId) {
                  <li class="flex flex-wrap items-center gap-2">
                    <span class="flex-1">
                      {{ line.description || debtLabel(line.direction) }}
                      <span class="text-xs text-muted">· {{ line.occurredAt | date: 'mediumDate' }}</span>
                    </span>
                    <span class="tabular-nums">{{ line.paid | money: currency() }}</span>
                    @if (line.remainingAfter === 0) {
                      <ui-badge tone="success" i18n="@@payment.fullyPaid">Paid</ui-badge>
                    } @else {
                      <ui-badge tone="warning" i18n="@@payment.leftOf">{{ line.remainingAfter | money: currency() }} left of {{ line.debtAmount | money: currency() }}</ui-badge>
                    }
                  </li>
                }
                @if (p.extra > 0) {
                  <li class="flex items-center gap-2">
                    <span class="flex-1">
                      @if (p.extraDirection === 'OwedToMe') {
                        <ng-container i18n="@@payment.extraOwedToMe">Paid beyond what was owed: {{ contactName() // i18n(ph="name") }} now owes you</ng-container>
                      } @else {
                        <ng-container i18n="@@payment.extraIOwe">Paid beyond what was owed: you now owe {{ contactName() // i18n(ph="name") }}</ng-container>
                      }
                    </span>
                    <span class="tabular-nums">{{ p.extra | money: currency() }}</span>
                  </li>
                }
              </ul>
              <p class="mt-2 flex justify-between border-t border-border pt-2">
                <span class="text-muted" i18n="@@payment.balanceAfter">Balance afterwards</span>
                <span class="font-medium tabular-nums" [class]="amountClass(p.balanceAfter)">{{ p.balanceAfter | money: currency(): true }}</span>
              </p>
            } @else if (preview.error()) {
              <p class="text-danger" i18n="@@payment.previewFailed">The preview is not available. Check the values.</p>
            }
          </section>

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
export class PaymentDialog {
  private readonly api = inject(LedgerApi);
  private readonly tracker = inject(OperationTracker);
  private readonly data = inject<PaymentDialogData | null>(DIALOG_DATA, { optional: true }) ?? {};
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly edit = 'edit' in this.data ? this.data.edit : null;
  protected readonly saving = signal(false);
  protected readonly currencies = CURRENCIES;
  protected readonly amountClass = amountClass;

  protected readonly heading = this.edit
    ? $localize`:@@payment.editTitle:Change the payment`
    : $localize`:@@payment.title:Record a payment`;
  protected readonly submitLabel = this.edit ? $localize`:@@ui.save:Save` : $localize`:@@payment.submit:Record payment`;

  /** Only whoever recorded it chose (and may change) the amount and the wallet. */
  private readonly mine = !this.edit || this.edit.recordedByMe;
  protected readonly asksWallet = this.mine;

  protected readonly contacts = rxResource({ stream: () => this.api.contacts() });
  private readonly allWallets = rxResource({ stream: () => this.api.wallets() });

  private readonly start = 'edit' in this.data ? null : this.data;

  protected readonly form = inject(NonNullableFormBuilder).group({
    contactId: [this.start?.contactId ?? '', this.edit ? [] : Validators.required],
    currency: [(this.edit?.currency ?? this.start?.currency ?? 'EUR') as Currency],
    direction: [(this.start?.direction ?? 'TheyPaidMe') as PaymentDirection],
    amount: [
      { value: this.edit?.total ?? this.start?.amount ?? (null as number | null), disabled: !this.mine },
      [Validators.required, Validators.min(0.01)],
    ],
    walletId: [this.edit?.walletId ?? '', this.mine ? Validators.required : []],
    description: [this.edit?.description ?? ''],
    occurredAt: [this.edit?.occurredAt ?? nowIso(), Validators.required],
    reason: ['', this.edit ? [Validators.required, Validators.pattern(/\S/)] : []],
  });

  private readonly value = toSignal(
    this.form.valueChanges.pipe(
      debounceTime(250),
      map(() => this.form.getRawValue()),
    ),
    { initialValue: this.form.getRawValue() },
  );

  protected readonly currency = computed(() => this.value().currency);

  protected readonly contactName = computed(() => {
    if (this.edit) return this.edit.counterpartyName ?? '';
    const id = this.value().contactId;
    return this.contacts.value()?.find((c) => c.id === id)?.name ?? $localize`:@@payment.theContact:the contact`;
  });

  protected readonly wallets = computed(() =>
    (this.allWallets.value() ?? []).filter((w) => w.currency === this.currency()),
  );

  protected readonly walletLabel = computed(() => {
    const iPay = this.edit ? this.edit.fromPersonId !== this.edit.counterpartyPersonId : this.value().direction === 'IPaidThem';
    return iPay
      ? $localize`:@@payment.fromWallet:From wallet`
      : $localize`:@@payment.toWallet:Into wallet`;
  });

  private readonly previewRequest = computed<PreviewPaymentRequest | null>(() => {
    const value = this.value();
    const amount = Number(value.amount);
    if (!(amount > 0)) return null;

    if (this.edit) return { amount, operationId: this.edit.id };
    if (!value.contactId) return null;

    return { amount, contactId: value.contactId, currency: value.currency, direction: value.direction };
  });

  protected readonly preview = rxResource<PaymentPreview | null, PreviewPaymentRequest | null>({
    params: () => this.previewRequest(),
    stream: ({ params }) => (params ? this.api.previewPayment(params) : of(null)),
  });

  protected debtLabel(direction: 'OwedToMe' | 'IOwe'): string {
    return direction === 'OwedToMe'
      ? $localize`:@@payment.debtOwedToMe:What they owed you`
      : $localize`:@@payment.debtIOwe:What you owed`;
  }

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
            amount: this.mine ? Number(value.amount) : null,
            walletId: this.mine ? value.walletId : null,
          }),
          $localize`:@@change.applied:Change applied.`,
        );
      } else {
        await this.tracker.submit(
          this.api.recordPayment({
            contactId: value.contactId,
            currency: value.currency,
            direction: value.direction,
            amount: Number(value.amount),
            walletId: value.walletId,
            occurredAt: value.occurredAt,
            description: value.description.trim() || null,
          }),
          $localize`:@@payment.recorded:Payment recorded.`,
        );
      }

      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}
