import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { LedgerApi } from '../../core/api/ledger-api';
import { Currency } from '../../core/api/models';
import { Button, DialogLayout, EmptyState, FormField, Input, MoneyPipe } from '../../ui';

export interface WalletPickerData {
  title: string;
  message: string;
  currency: Currency;
  confirmLabel: string;
}

/** Asks which wallet (in a given currency) money came from or went to. Closes with the wallet id. */
@Component({
  selector: 'app-wallet-picker-dialog',
  imports: [FormsModule, DialogLayout, FormField, Input, Button, EmptyState, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [heading]="data.title" [description]="data.message">
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
      <div dialog-actions class="contents">
        <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
        <button uiButton type="button" [disabled]="!walletId()" (click)="ref.close(walletId())">{{ data.confirmLabel }}</button>
      </div>
    </ui-dialog>
  `,
})
export class WalletPickerDialog {
  private readonly api = inject(LedgerApi);
  protected readonly data = inject<WalletPickerData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<string>>(DialogRef);
  protected readonly walletId = signal('');

  protected readonly allWallets = rxResource({ stream: () => this.api.wallets() });
  protected readonly wallets = computed(() =>
    (this.allWallets.value() ?? []).filter((w) => w.currency === this.data.currency && w.isReady),
  );
}
