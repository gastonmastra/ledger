import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { OperationDetails } from '../../core/api/models';
import { CurrentUser } from '../../core/current-user';
import { operationKindLabel } from '../../core/i18n/labels';
import { OperationTracker } from '../../core/operations/operation-tracker';
import { ConfirmService, DialogService } from '../../ui';
import { SplitDialog, SplitDialogData, SplitPerson } from '../shared/split-dialog';
import { LendDialog } from './debt-dialogs';
import { InitialDebtDialog } from './initial-debt-dialog';
import { OperationHistoryDialog, OperationHistoryResult } from './operation-history-dialog';
import { PaymentDialog, PaymentDialogData } from './payment-dialog';
import { SettlementEditDialog } from './settlement-edit-dialog';

/**
 * Edit, delete, restore and history of loans, existing debts, splits and settlements, shared by
 * every page that lists them. Changes are new versions; linked people involved confirm them.
 */
@Injectable({ providedIn: 'root' })
export class OperationActions {
  private readonly api = inject(LedgerApi);
  private readonly dialogs = inject(DialogService);
  private readonly confirm = inject(ConfirmService);
  private readonly tracker = inject(OperationTracker);
  private readonly currentUser = inject(CurrentUser);

  async edit(operation: OperationDetails): Promise<void> {
    switch (operation.kind) {
      case 'Loan':
        await this.dialogs.open(LendDialog, operation);
        break;
      case 'InitialDebt':
        await this.dialogs.open(InitialDebtDialog, operation);
        break;
      case 'Settlement':
        // Outside groups it is a payment: its amount can change. A group settlement only its date and wallet.
        if (operation.groupId) await this.dialogs.open(SettlementEditDialog, operation);
        else await this.dialogs.open<boolean, PaymentDialogData>(PaymentDialog, { edit: operation }, 'lg');
        break;
      case 'Split':
        await this.dialogs.open<boolean, SplitDialogData>(
          SplitDialog,
          { groupId: operation.groupId, people: await this.splitPeople(operation), edit: operation },
          'lg',
        );
        break;
    }
  }

  async delete(operation: OperationDetails): Promise<void> {
    const kind = operationKindLabel(operation.kind).toLowerCase();
    const reason = await this.confirm.askReason({
      title: $localize`:@@operations.deleteTitle:Delete this ${kind}:kind:?`,
      message:
        operation.kind === 'Settlement'
          ? $localize`:@@operations.deleteSettlement:What it paid will be owed again. It stays in the history.`
          : $localize`:@@operations.deleteMessage:It stays in the history and can be restored. Linked people involved have to confirm it.`,
      confirmLabel: $localize`:@@ui.delete:Delete`,
      danger: true,
    });

    if (reason !== null) {
      await this.tracker.submitChange(
        this.api.deleteOperation(operation.id, reason),
        $localize`:@@operations.deleted:Deleted.`,
      );
    }
  }

  async restore(operation: OperationDetails): Promise<void> {
    const reason = await this.confirm.askReason({
      title: $localize`:@@operations.restoreTitle:Restore it?`,
      message: $localize`:@@operations.restoreMessage:It counts again as it was before it was deleted. Linked people involved have to confirm it.`,
      confirmLabel: $localize`:@@history.restore:Restore`,
    });

    if (reason !== null) {
      await this.tracker.submitChange(
        this.api.restoreOperation(operation.id, reason),
        $localize`:@@operations.restored:Restored.`,
      );
    }
  }

  async history(operationId: string): Promise<void> {
    const result = await this.dialogs.open<OperationHistoryResult, string>(OperationHistoryDialog, operationId, 'lg');
    if (result?.restore) await this.restore(result.restore);
  }

  /** A paid debt cannot change: offers to undo (delete) the payment that paid it. */
  async undoSettlement(operation: OperationDetails): Promise<void> {
    const settlementId = operation.settlementOperationIds[0];
    if (!settlementId) return;

    await this.delete(await firstValueFrom(this.api.operation(settlementId)));
  }

  /** People to choose from when changing a split: its own people plus mine (or the group's members). */
  private async splitPeople(operation: OperationDetails): Promise<SplitPerson[]> {
    const me = this.currentUser.me.value() ?? (await firstValueFrom(this.api.me()));
    const people = new Map<string, SplitPerson>();

    if (operation.groupId) {
      const group = await firstValueFrom(this.api.group(operation.groupId));
      for (const member of group.members) {
        people.set(member.personId, { personId: member.personId, name: member.displayName, isMe: member.isMe });
      }
    } else {
      for (const person of [...operation.payers, ...operation.shares]) {
        people.set(person.personId, { personId: person.personId, name: person.name, isMe: person.personId === me.personId });
      }

      // Only whoever recorded it can add their own contacts (the others are not in their circle).
      if (operation.recordedByMe) {
        for (const contact of await firstValueFrom(this.api.contacts())) {
          if (!people.has(contact.personId)) {
            people.set(contact.personId, { personId: contact.personId, name: contact.name, isMe: false });
          }
        }
      }
    }

    if (!people.has(me.personId)) {
      people.set(me.personId, { personId: me.personId, name: me.displayName, isMe: true });
    }

    // Me first, like when a split is recorded.
    return [...people.values()].sort((a, b) => Number(b.isMe) - Number(a.isMe));
  }
}
