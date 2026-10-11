import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { LedgerApi } from '../../core/api/ledger-api';
import { OperationDetails } from '../../core/api/models';
import { DataChanges } from '../../core/data-changes';
import { operationKindLabel, operationStatusLabel } from '../../core/i18n/labels';
import { Badge, Button, Card, EmptyState, Icon, MoneyPipe, Pagination, Spinner } from '../../ui';
import { OperationActions } from './operation-actions';

const PAGE_SIZE = 10;

/** Loans, existing debts, splits and settlements (of a group, if given), with edit, delete and history. */
@Component({
  selector: 'app-operations-list',
  imports: [DatePipe, RouterLink, Card, Badge, Button, Icon, EmptyState, Spinner, Pagination, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-card [heading]="heading()" [padded]="false">
      <label card-actions class="flex cursor-pointer items-center gap-2 text-sm text-muted">
        <input type="checkbox" class="accent-primary" [checked]="includeHistory()" (change)="toggleHistory()" />
        <ng-container i18n="@@operations.showChanged">Show changed and deleted</ng-container>
      </label>

      @if (operations.isLoading() && !operations.value()) {
        <div class="flex justify-center py-8"><ui-spinner /></div>
      } @else if (!operations.value()?.items?.length) {
        <ui-empty-state i18n-message="@@operations.empty" message="Nothing recorded yet." />
      } @else {
        @let result = operations.value()!;
        <ul class="divide-y divide-border">
          @for (operation of result.items; track operation.id) {
            <li class="flex flex-wrap items-start gap-3 px-4 py-3" [class.opacity-60]="!isCurrent(operation)">
              <div class="min-w-0 flex-1">
                <div class="flex flex-wrap items-center gap-2">
                  <span class="text-sm text-muted tabular-nums">{{ operation.occurredAt | date: 'medium' }}</span>
                  <ui-badge tone="primary">{{ kindLabel(operation.kind) }}</ui-badge>
                  @if (operation.groupName && !groupId()) {
                    <ui-badge>{{ operation.groupName }}</ui-badge>
                  }
                  @if (!isCurrent(operation)) {
                    <ui-badge [tone]="operation.status === 'Deleted' || operation.status === 'Rejected' ? 'danger' : 'neutral'">{{ statusLabel(operation.status) }}</ui-badge>
                  }
                  @if (operation.pendingChange; as change) {
                    @if (change.waitingForMe) {
                      <a routerLink="/changes"><ui-badge tone="warning" i18n="@@operations.waitingForYou">Change waiting for you</ui-badge></a>
                    } @else {
                      <ui-badge tone="warning" i18n="@@operations.changePending">Change pending</ui-badge>
                    }
                  }
                  @if (operation.isDisputed) {
                    <ui-badge tone="danger" i18n="@@operations.disputed">Disputed</ui-badge>
                  }
                  @if (operation.blockedReason === 'debt.settled') {
                    <ui-badge i18n="@@operations.hasPayments">Has payments</ui-badge>
                  }
                </div>
                <p class="mt-1 text-sm font-medium">{{ operation.description || summary(operation) }}</p>
                <p class="text-xs text-muted">
                  {{ summary(operation) }}
                  @if (!operation.recordedByMe) {
                    · <ng-container i18n="@@operations.recordedBy">recorded by {{ operation.recordedByName // i18n(ph="name") }}</ng-container>
                  }
                </p>
              </div>
              <span class="text-sm font-medium tabular-nums">{{ operation.total | money: operation.currency }}</span>
              <div class="flex w-full flex-wrap justify-end gap-1 sm:w-auto">
                @if (operation.canEdit) {
                  <button uiButton variant="ghost" size="sm" type="button" (click)="actions.edit(operation)">
                    <ui-icon name="pencil" size="sm" /><ng-container i18n="@@ui.edit">Edit</ng-container>
                  </button>
                }
                @if (operation.canDelete) {
                  <button uiButton variant="ghost" size="sm" type="button" (click)="actions.delete(operation)">
                    <ui-icon name="trash" size="sm" /><ng-container i18n="@@ui.delete">Delete</ng-container>
                  </button>
                }
                @if (operation.blockedReason === 'debt.settled' && operation.status === 'Completed') {
                  <button uiButton variant="ghost" size="sm" type="button" (click)="actions.undoSettlement(operation)"
                          i18n-title="@@operations.undoPaymentHint" title="It was paid (fully or partly): undo the payment to change it.">
                    <ui-icon name="undo" size="sm" /><ng-container i18n="@@operations.undoPayment">Undo payment</ng-container>
                  </button>
                }
                @if (operation.canRestore) {
                  <button uiButton variant="ghost" size="sm" type="button" (click)="actions.restore(operation)">
                    <ui-icon name="undo" size="sm" /><ng-container i18n="@@history.restore">Restore</ng-container>
                  </button>
                }
                <button uiButton variant="ghost" size="sm" type="button" (click)="actions.history(operation.id)">
                  <ui-icon name="clock" size="sm" /><ng-container i18n="@@history.title">History</ng-container>
                </button>
              </div>
            </li>
          }
        </ul>
        <ui-pagination class="border-t border-border px-4 py-3" [page]="result.page" [totalPages]="result.totalPages"
                       [totalCount]="result.totalCount" (pageChange)="page.set($event)" />
      }
    </ui-card>
  `,
})
export class OperationsList {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);
  protected readonly actions = inject(OperationActions);

  readonly heading = input.required<string>();
  /** Only this group's expenses and payments; without it, everything outside groups too. */
  readonly groupId = input<string | null>(null);

  protected readonly page = signal(1);
  protected readonly includeHistory = signal(false);
  protected readonly kindLabel = operationKindLabel;
  protected readonly statusLabel = operationStatusLabel;

  protected readonly operations = rxResource({
    params: () => ({
      groupId: this.groupId(),
      page: this.page(),
      includeHistory: this.includeHistory(),
      version: this.changes.version(),
    }),
    stream: ({ params }) =>
      this.api.operations({
        groupId: params.groupId,
        page: params.page,
        pageSize: PAGE_SIZE,
        includeHistory: params.includeHistory,
      }),
  });

  protected toggleHistory(): void {
    this.includeHistory.update((v) => !v);
    this.page.set(1);
  }

  protected isCurrent(operation: OperationDetails): boolean {
    return operation.status === 'Completed' || operation.status === 'Pending';
  }

  /** Who and how much, in a few words. */
  protected summary(operation: OperationDetails): string {
    switch (operation.kind) {
      case 'Split': {
        const payers = operation.payers.map((p) => p.name).join(', ');
        const people = operation.shares.length;
        return $localize`:@@operations.splitSummary:Paid by ${payers}:payers:, shared by ${people}:count: people`;
      }
      default:
        return `${operation.fromName ?? ''} → ${operation.toName ?? ''}`;
    }
  }
}
