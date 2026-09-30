import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { LedgerApi } from '../../core/api/ledger-api';
import { Currency } from '../../core/api/models';
import { Button, DateTimeInput, DialogLayout, EmptyState, FormField, Input, MoneyPipe, nowIso } from '../../ui';

export interface WalletPickerData {
  title: string;
  message: string;
  currency: Currency;
  confirmLabel: string;
  /** False when no money of mine moves (only the date is asked). Default: true. */
  walletRequired?: boolean;
  /** Initial date (default: now). */
  occurredAt?: string;
}

export interface WalletPickerResult {
  walletId: string | null;
  occurredAt: string;
}

/**
 * Asks which wallet (in a given currency) money came from or went to, and when.
 * Used by settlements and pending wallet assignments.
 */
@Component({
  selector: 'app-wallet-picker-dialog',
  imports: [FormsModule, DialogLayout, FormField, Input, Button, EmptyState, MoneyPipe, DateTimeInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [heading]="data.title" [description]="data.message">
      <div class="grid gap-4">
        @if (walletRequired) {
          @if (wallets().length === 0 && !allWallets.isLoading()) {
            <ui-empty-state i18n-message="@@picker.noWallets" message="You have no {{ data.currency }} wallet. Create one first." />
          } @else {
            <ui-form-field i18n-label="@@picker.wallet" label="Wallet">
              <select uiInput [ngModel]="walletId()" (ngModelChange)="walletId.set($event)">
                <option value="" disabled i18n="@@ui.choose">Choose…</option>
                @for (wallet of wallets(); track wallet.id) {
                  <option [value]="wallet.id">{{ wallet.name }} · {{ wallet.balance | money: wallet.currency }}</option>
                }
              </select>
            </ui-form-field>
          }
        }
        <ui-form-field i18n-label="@@ui.when" label="When">
          <ui-datetime-input [ngModel]="occurredAt()" (ngModelChange)="occurredAt.set($event)" />
        </ui-form-field>
      </div>
      <div dialog-actions class="contents">
        <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
        <button uiButton type="button" [disabled]="!canConfirm()" (click)="confirm()">{{ data.confirmLabel }}</button>
      </div>
    </ui-dialog>
  `,
})
export class WalletPickerDialog {
  private readonly api = inject(LedgerApi);
  protected readonly data = inject<WalletPickerData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<WalletPickerResult>>(DialogRef);
  protected readonly walletRequired = this.data.walletRequired ?? true;
  protected readonly walletId = signal('');
  protected readonly occurredAt = signal(this.data.occurredAt ?? nowIso());

  protected readonly allWallets = rxResource({ stream: () => this.api.wallets() });
  protected readonly wallets = computed(() =>
    (this.allWallets.value() ?? []).filter((w) => w.currency === this.data.currency && w.isReady),
  );

  protected readonly canConfirm = computed(() => (!this.walletRequired || !!this.walletId()) && !!this.occurredAt());

  protected confirm(): void {
    this.ref.close({ walletId: this.walletRequired ? this.walletId() : null, occurredAt: this.occurredAt() });
  }
}
