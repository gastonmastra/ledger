import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { PendingAssignment } from '../../core/api/models';
import { DataChanges } from '../../core/data-changes';
import { ToastService } from '../../core/notifications/toast.service';
import { Button, Card, DialogService, EmptyState, MoneyPipe, PageHeader, Spinner, Table } from '../../ui';
import { WalletPickerDialog, WalletPickerData, WalletPickerResult } from '../shared/wallet-picker-dialog';

/** Money that other users recorded on my behalf: I choose the wallet it came from or went to. */
@Component({
  selector: 'app-pending-page',
  imports: [DatePipe, PageHeader, Card, Table, Button, EmptyState, Spinner, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-page-header i18n-heading="@@pending.title" heading="Pending wallet assignments"
                    i18n-subtitle="@@pending.subtitle" subtitle="Other people recorded money you paid or received. Tell us which wallet it was." />

    <ui-card [padded]="false">
      @if (pending.isLoading() && !pending.value()) {
        <div class="flex justify-center py-10"><ui-spinner /></div>
      } @else if (!pending.value()?.length) {
        <ui-empty-state i18n-message="@@pending.empty" message="Nothing pending." />
      } @else {
        <ui-table>
          <thead>
            <tr>
              <th i18n="@@pending.when">When</th>
              <th i18n="@@pending.what">What</th>
              <th i18n="@@pending.with">With</th>
              <th class="text-right" i18n="@@pending.money">Money</th>
              <th><span class="sr-only" i18n="@@ui.actions">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (item of pending.value(); track item.id) {
              <tr>
                <td class="text-muted">{{ item.occurredAt | date: 'mediumDate' }}</td>
                <td>{{ item.description }}</td>
                <td>{{ item.counterpartyName }}</td>
                <td class="text-right tabular-nums">
                  @if (item.amount > 0) {
                    <span class="text-success" i18n="@@pending.received">received {{ item.amount | money: item.currency }}</span>
                  } @else {
                    <span class="text-danger" i18n="@@pending.paid">paid {{ -item.amount | money: item.currency }}</span>
                  }
                </td>
                <td class="text-right">
                  <button uiButton size="sm" type="button" (click)="assign(item)" i18n="@@pending.chooseWallet">Choose wallet</button>
                </td>
              </tr>
            }
          </tbody>
        </ui-table>
      }
    </ui-card>
  `,
})
export class PendingPage {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);
  private readonly dialogs = inject(DialogService);
  private readonly toasts = inject(ToastService);

  protected readonly pending = rxResource({
    params: () => this.changes.version(),
    stream: () => this.api.pendingAssignments(),
  });

  protected async assign(item: PendingAssignment): Promise<void> {
    const choice = await this.dialogs.open<WalletPickerResult, WalletPickerData>(WalletPickerDialog, {
      currency: item.currency,
      occurredAt: item.occurredAt,
      title: $localize`:@@pending.chooseWallet:Choose wallet`,
      message:
        item.amount > 0
          ? $localize`:@@pending.whereReceived:Which wallet received the money?`
          : $localize`:@@pending.wherePaid:Which wallet did you pay from?`,
      confirmLabel: $localize`:@@ui.save:Save`,
    });

    if (choice?.walletId) {
      await firstValueFrom(this.api.assignWallet(item.id, choice.walletId, choice.occurredAt));
      this.toasts.success($localize`:@@pending.assigned:Recorded in your wallet.`);
      this.changes.notify();
    }
  }
}
