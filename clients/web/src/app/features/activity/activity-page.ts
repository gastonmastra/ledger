import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { forkJoin } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { DataChanges } from '../../core/data-changes';
import {
  changeActionLabel,
  changeStatusLabel,
  operationKindLabel,
  transactionKindLabel,
} from '../../core/i18n/labels';
import { Badge, Button, Card, EmptyState, Icon, PageHeader, Spinner } from '../../ui';
import { OperationActions } from '../debts/operation-actions';

interface ActivityRow {
  at: string;
  kind: string;
  action: string;
  description: string | null;
  reason: string;
  by: string | null;
  status: string | null;
  disputed: boolean;
  /** Shared operations open their history. */
  operationId: string | null;
}

const PAGE_SIZE = 25;

/** Recent edits, deletions and restores: of my own transactions and of operations I share. */
@Component({
  selector: 'app-activity-page',
  imports: [DatePipe, PageHeader, Card, Badge, Button, Icon, EmptyState, Spinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-page-header i18n-heading="@@activity.title" heading="Activity"
                    i18n-subtitle="@@activity.subtitle" subtitle="What was changed, deleted or restored, by whom and why." />

    <ui-card [padded]="false">
      @if (activity.isLoading() && !activity.value()) {
        <div class="flex justify-center py-10"><ui-spinner /></div>
      } @else if (!rows().length) {
        <ui-empty-state i18n-message="@@activity.empty" message="Nothing has been changed yet." />
      } @else {
        <ul class="divide-y divide-border">
          @for (row of rows(); track $index) {
            <li class="flex flex-wrap items-start gap-3 px-4 py-3">
              <div class="min-w-0 flex-1">
                <div class="flex flex-wrap items-center gap-2">
                  <span class="text-sm text-muted tabular-nums">{{ row.at | date: 'medium' }}</span>
                  <ui-badge tone="primary">{{ row.kind }}</ui-badge>
                  <span class="text-sm font-medium">{{ row.action }}</span>
                  @if (row.status) {
                    <ui-badge tone="neutral">{{ row.status }}</ui-badge>
                  }
                  @if (row.disputed) {
                    <ui-badge tone="danger" i18n="@@operations.disputed">Disputed</ui-badge>
                  }
                </div>
                <p class="mt-1 text-sm">
                  @if (row.description) {
                    {{ row.description }} ·
                  }
                  “{{ row.reason }}”
                  @if (row.by) {
                    <span class="text-muted" i18n="@@history.byName">by {{ row.by // i18n(ph="name") }}</span>
                  }
                </p>
              </div>
              @if (row.operationId) {
                <button uiButton variant="ghost" size="sm" type="button" (click)="operations.history(row.operationId)">
                  <ui-icon name="clock" size="sm" /><ng-container i18n="@@history.title">History</ng-container>
                </button>
              }
            </li>
          }
        </ul>
        @if (hasMore()) {
          <div class="border-t border-border px-4 py-3 text-center">
            <button uiButton variant="secondary" size="sm" type="button" (click)="size.set(size() + PAGE_SIZE)" i18n="@@activity.more">Show more</button>
          </div>
        }
      }
    </ui-card>
  `,
})
export class ActivityPage {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);
  protected readonly operations = inject(OperationActions);
  protected readonly PAGE_SIZE = PAGE_SIZE;

  /** How many of each source are loaded (both are merged by date). */
  protected readonly size = signal(PAGE_SIZE);

  protected readonly activity = rxResource({
    params: () => ({ size: this.size(), version: this.changes.version() }),
    stream: ({ params }) =>
      forkJoin({
        ledger: this.api.transactionActivity(1, params.size),
        debts: this.api.debtActivity(1, params.size),
      }),
  });

  protected readonly rows = computed<ActivityRow[]>(() => {
    const data = this.activity.value();
    if (!data) return [];

    const ledger = data.ledger.items.map<ActivityRow>((item) => ({
      at: item.at,
      kind: transactionKindLabel(item.kind),
      action: changeActionLabel(item.action),
      description: item.description,
      reason: item.reason,
      by: item.changedByMe ? null : item.changedByName,
      status: null,
      disputed: false,
      operationId: null,
    }));

    const debts = data.debts.items.map<ActivityRow>((change) => ({
      at: change.createdAt,
      kind: operationKindLabel(change.operationKind),
      action: changeActionLabel(change.kind),
      description: (change.proposed ?? change.current).description,
      reason: change.reason,
      by: change.requestedByMe ? null : change.requestedByName,
      status: changeStatusLabel(change.status),
      disputed: change.disputed,
      operationId: change.operationId,
    }));

    return [...ledger, ...debts].sort((a, b) => new Date(b.at).getTime() - new Date(a.at).getTime());
  });

  protected readonly hasMore = computed(() => {
    const data = this.activity.value();
    return !!data && (data.ledger.totalCount > this.size() || data.debts.totalCount > this.size());
  });
}
