import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Wallet } from '../../core/api/models';
import { walletStatusLabel } from '../../core/i18n/labels';
import { Badge, Button, DialogLayout, Icon, MoneyPipe } from '../../ui';

export type WalletAction = 'opening' | 'adjust' | 'rename' | 'archive' | 'delete';

/** A wallet and what can be done with it (only active wallets can change). */
@Component({
  selector: 'app-wallet-detail-dialog',
  imports: [DialogLayout, Badge, Button, Icon, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [heading]="wallet.name">
      <div class="grid gap-4 text-sm">
        <p class="text-3xl font-semibold tabular-nums">{{ wallet.balance | money: wallet.currency }}</p>
        <div class="flex flex-wrap items-center gap-2">
          <ui-badge>{{ wallet.currency }}</ui-badge>
          @if (!wallet.isReady) {
            <ui-badge tone="warning" i18n="@@wallets.settingUp">Setting up…</ui-badge>
          } @else {
            <ui-badge [tone]="wallet.status === 'Active' ? 'success' : 'neutral'">{{ statusLabel(wallet.status) }}</ui-badge>
          }
        </div>
        @if (active && wallet.isReady) {
          <div class="grid gap-2 sm:grid-cols-2">
            <button uiButton variant="secondary" type="button" (click)="ref.close('opening')" i18n="@@wallets.openingAction">Opening balance</button>
            <button uiButton variant="secondary" type="button" (click)="ref.close('adjust')" i18n="@@wallets.adjustAction">Adjust balance</button>
          </div>
        }
      </div>
      <div dialog-actions class="contents">
        @if (active) {
          <button uiButton variant="ghost" type="button" (click)="ref.close('delete')">
            <ui-icon name="trash" size="sm" /><ng-container i18n="@@ui.delete">Delete</ng-container>
          </button>
          <button uiButton variant="secondary" type="button" (click)="ref.close('archive')">
            <ui-icon name="archive" size="sm" /><ng-container i18n="@@wallets.archive">Archive</ng-container>
          </button>
          <button uiButton type="button" (click)="ref.close('rename')">
            <ui-icon name="pencil" size="sm" /><ng-container i18n="@@ui.rename">Rename</ng-container>
          </button>
        } @else {
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.close">Close</button>
        }
      </div>
    </ui-dialog>
  `,
})
export class WalletDetailDialog {
  protected readonly wallet = inject<Wallet>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<WalletAction>>(DialogRef);
  protected readonly active = this.wallet.status === 'Active';
  protected readonly statusLabel = walletStatusLabel;
}
