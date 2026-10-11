import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { LedgerApi } from '../../core/api/ledger-api';
import { OperationDetails } from '../../core/api/models';
import {
  approvalStatusLabel,
  changeActionLabel,
  changeStatusLabel,
  operationKindLabel,
  operationStatusLabel,
} from '../../core/i18n/labels';
import { Badge, BadgeTone, Button, DialogLayout, Icon, MoneyPipe, Spinner } from '../../ui';

/** Closes with the version to restore, when the user asks for it. */
export type OperationHistoryResult = { restore: OperationDetails } | undefined;

/** Every version of a loan, existing debt, split or settlement, and every change requested on it. */
@Component({
  selector: 'app-operation-history-dialog',
  imports: [DatePipe, DialogLayout, Badge, Button, Icon, MoneyPipe, Spinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog i18n-heading="@@history.title" heading="History"
               i18n-description="@@history.operationDescription" description="Nothing is edited in place: every change is a new version, with who asked for it and why.">
      @if (history.isLoading() && !history.value()) {
        <div class="flex justify-center py-6"><ui-spinner /></div>
      } @else if (history.value(); as data) {
        <h3 class="mb-2 text-sm font-semibold" i18n="@@history.versions">Versions</h3>
        <ol class="mb-5 grid gap-2">
          @for (version of data.versions; track version.id) {
            <li class="rounded-ui border border-border p-3 text-sm">
              <div class="flex flex-wrap items-center gap-2">
                <span class="font-medium" i18n="@@history.revision">Version {{ version.revision }}</span>
                <ui-badge [tone]="tone(version)">{{ statusLabel(version.status) }}</ui-badge>
                @if (version.isDisputed) {
                  <ui-badge tone="danger" i18n="@@operations.disputed">Disputed</ui-badge>
                }
                <span class="ml-auto font-medium tabular-nums">{{ version.total | money: version.currency }}</span>
              </div>
              <p class="mt-1 text-muted">
                {{ kindLabel(version.kind) }} · {{ version.occurredAt | date: 'medium' }}
                @if (version.description) {
                  · {{ version.description }}
                }
              </p>
              @if (version.allocations?.length || version.extraAmount) {
                <ul class="mt-1 grid gap-0.5 text-xs text-muted">
                  @for (allocation of version.allocations; track allocation.debtId) {
                    <li i18n="@@history.paidDebt">Paid {{ allocation.amount | money: version.currency // i18n(ph="amount") }} of {{ allocation.description || '—' // i18n(ph="debt") }} ({{ allocation.debtAmount | money: version.currency // i18n(ph="total") }})</li>
                  }
                  @if (version.extraAmount) {
                    <li i18n="@@history.extraDebt">{{ version.extraAmount | money: version.currency // i18n(ph="amount") }} beyond what was owed became a new debt</li>
                  }
                </ul>
              }
              @if (version.shares.length) {
                <p class="mt-1 text-xs text-muted">
                  @for (share of version.shares; track share.personId; let last = $last) {
                    {{ share.name }} {{ share.amount | money: version.currency }}@if (!last) {, }
                  }
                </p>
              }
              @if (version.rejectionReason) {
                <p class="mt-1 text-xs text-danger">{{ version.rejectionReason }}</p>
              }
              @if (version.canRestore) {
                <button uiButton variant="secondary" size="sm" type="button" class="mt-2" (click)="ref.close({ restore: version })">
                  <ui-icon name="undo" size="sm" /><ng-container i18n="@@history.restore">Restore</ng-container>
                </button>
              }
            </li>
          }
        </ol>

        <h3 class="mb-2 text-sm font-semibold" i18n="@@history.changes">Changes</h3>
        @if (!data.changes.length) {
          <p class="text-sm text-muted" i18n="@@history.noChanges">It has not been changed.</p>
        }
        <ol class="grid gap-2">
          @for (change of data.changes; track change.id) {
            <li class="rounded-ui border border-border p-3 text-sm">
              <div class="flex flex-wrap items-center gap-2">
                <span class="font-medium">{{ actionLabel(change.kind) }}</span>
                <ui-badge [tone]="change.status === 'Failed' ? 'danger' : change.status === 'Applied' ? 'success' : 'warning'">
                  {{ changeStatus(change.status) }}
                </ui-badge>
                @if (change.disputed) {
                  <ui-badge tone="danger" i18n="@@operations.disputed">Disputed</ui-badge>
                }
                <span class="ml-auto text-xs text-muted">{{ change.createdAt | date: 'medium' }}</span>
              </div>
              <p class="mt-1">
                <span class="text-muted" i18n="@@history.by">By {{ change.requestedByName // i18n(ph="name") }}:</span>
                “{{ change.reason }}”
              </p>
              @if (change.failureReason) {
                <p class="mt-1 text-xs text-danger">{{ change.failureReason }}</p>
              }
              @if (change.approvals.length) {
                <ul class="mt-2 grid gap-1 text-xs">
                  @for (approval of change.approvals; track approval.userId) {
                    <li>
                      {{ approval.name }}: {{ approvalLabel(approval.status) }}
                      @if (approval.reason) {
                        — “{{ approval.reason }}”
                      }
                    </li>
                  }
                </ul>
              }
            </li>
          }
        </ol>
      }
      <div dialog-actions class="contents">
        <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.close">Close</button>
      </div>
    </ui-dialog>
  `,
})
export class OperationHistoryDialog {
  private readonly api = inject(LedgerApi);
  private readonly operationId = inject<string>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<OperationHistoryResult>>(DialogRef);

  protected readonly kindLabel = operationKindLabel;
  protected readonly statusLabel = operationStatusLabel;
  protected readonly actionLabel = changeActionLabel;
  protected readonly changeStatus = changeStatusLabel;
  protected readonly approvalLabel = approvalStatusLabel;

  protected readonly history = rxResource({ stream: () => this.api.operationHistory(this.operationId) });

  protected tone(version: OperationDetails): BadgeTone {
    switch (version.status) {
      case 'Completed':
        return 'success';
      case 'Deleted':
      case 'Rejected':
        return 'danger';
      default:
        return 'neutral';
    }
  }
}
