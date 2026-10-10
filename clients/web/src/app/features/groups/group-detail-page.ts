import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { GroupMember, SuggestedPayment } from '../../core/api/models';
import { DataChanges } from '../../core/data-changes';
import { meLabel } from '../../core/i18n/labels';
import { OperationTracker } from '../../core/operations/operation-tracker';
import { ToastService } from '../../core/notifications/toast.service';
import {
  Badge,
  Button,
  Card,
  ConfirmService,
  DialogService,
  EmptyState,
  Icon,
  MoneyPipe,
  PageHeader,
  Spinner,
  Table,
} from '../../ui';
import { amountClass } from '../shared/amount';
import { SplitDialog, SplitDialogData } from '../shared/split-dialog';
import { WalletPickerDialog, WalletPickerData, WalletPickerResult } from '../shared/wallet-picker-dialog';
import { OperationsList } from '../debts/operations-list';
import { AddMemberDialog } from './group-dialogs';

@Component({
  selector: 'app-group-detail-page',
  imports: [RouterLink, PageHeader, Card, Table, Badge, Button, Icon, EmptyState, Spinner, MoneyPipe, OperationsList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './group-detail-page.html',
})
export class GroupDetailPage {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);
  private readonly dialogs = inject(DialogService);
  private readonly confirm = inject(ConfirmService);
  private readonly tracker = inject(OperationTracker);
  private readonly toasts = inject(ToastService);

  /** Route parameter (component input binding). */
  readonly id = input.required<string>();

  protected readonly amountClass = amountClass;

  protected readonly group = rxResource({
    params: () => ({ id: this.id(), version: this.changes.version() }),
    stream: ({ params }) => this.api.group(params.id),
  });

  protected readonly balances = rxResource({
    params: () => ({ id: this.id(), version: this.changes.version() }),
    stream: ({ params }) => this.api.groupBalances(params.id),
  });

  private readonly me = computed(() => this.group.value()?.members.find((m) => m.isMe) ?? null);

  /** Names come from the members (so "Me" is shown in the user's language). */
  protected name(personId: string): string {
    const member = this.group.value()?.members.find((m) => m.personId === personId);
    return member?.isMe ? meLabel() : (member?.displayName ?? '—');
  }

  protected memberName(member: GroupMember): string {
    return member.isMe ? meLabel() : member.displayName;
  }

  protected addExpense(): void {
    const group = this.group.value();
    if (!group) return;

    void this.dialogs.open<boolean, SplitDialogData>(SplitDialog, {
      groupId: group.id,
      people: group.members.map((m) => ({ personId: m.personId, name: m.displayName, isMe: m.isMe })),
    }, 'lg');
  }

  protected async addMember(): Promise<void> {
    if (await this.dialogs.open<boolean>(AddMemberDialog, this.group.value())) {
      this.toasts.success($localize`:@@groups.memberAdded:Member added.`);
      this.changes.notify();
    }
  }

  protected async removeMember(member: GroupMember): Promise<void> {
    const confirmed = await this.confirm.confirm({
      title: $localize`:@@groups.removeTitle:Remove member`,
      message: $localize`:@@groups.removeMessage:Remove ${member.displayName}:name: from the group?`,
      confirmLabel: $localize`:@@groups.remove:Remove`,
      danger: true,
    });

    if (confirmed) {
      await firstValueFrom(this.api.removeGroupMember(this.id(), member.personId));
      this.changes.notify();
    }
  }

  protected async settle(payment: SuggestedPayment): Promise<void> {
    const me = this.me()?.personId;
    const iPay = payment.fromPersonId === me;
    const iReceive = payment.toPersonId === me;
    // If I pay or receive, ask for the wallet; otherwise only the date.
    const choice = await this.dialogs.open<WalletPickerResult, WalletPickerData>(WalletPickerDialog, {
      currency: payment.currency,
      walletRequired: iPay || iReceive,
      title: $localize`:@@groups.settleTitle:Record payment`,
      message: iPay
        ? $localize`:@@groups.settlePay:Which wallet did you pay from?`
        : iReceive
          ? $localize`:@@groups.settleReceive:Which wallet received the money?`
          : $localize`:@@groups.settleOthers:Record that ${this.name(payment.fromPersonId)}:from: paid ${this.name(payment.toPersonId)}:to:?`,
      confirmLabel: $localize`:@@groups.settleConfirm:Record payment`,
    });

    if (!choice) return;

    await this.tracker.submit(
      this.api.settle({
        scope: 'GroupPair',
        groupId: this.id(),
        fromPersonId: payment.fromPersonId,
        toPersonId: payment.toPersonId,
        currency: payment.currency,
        walletId: choice.walletId,
        occurredAt: choice.occurredAt,
      }),
      $localize`:@@groups.settled:Payment recorded.`,
    );
  }
}
