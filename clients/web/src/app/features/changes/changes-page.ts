import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { OperationChange } from '../../core/api/models';
import { DataChanges } from '../../core/data-changes';
import {
  approvalStatusLabel,
  changeActionLabel,
  changeStatusLabel,
  operationKindLabel,
} from '../../core/i18n/labels';
import { ToastService } from '../../core/notifications/toast.service';
import { OperationTracker } from '../../core/operations/operation-tracker';
import { Badge, Button, Card, ConfirmService, EmptyState, Icon, MoneyPipe, PageHeader, Spinner } from '../../ui';
import { OperationActions } from '../debts/operation-actions';

/**
 * Changes to shared operations (loans, splits, settlements, existing debts) that need my answer.
 * Accepting updates my books; rejecting keeps my previous version and marks it as disputed.
 */
@Component({
  selector: 'app-changes-page',
  imports: [DatePipe, NgTemplateOutlet, PageHeader, Card, Badge, Button, Icon, EmptyState, Spinner, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-page-header i18n-heading="@@changes.title" heading="Changes to review"
                    i18n-subtitle="@@changes.subtitle" subtitle="Someone changed something you share. Nothing changes in your books until you answer.">
      <label class="flex cursor-pointer items-center gap-2 text-sm text-muted">
        <input type="checkbox" class="accent-primary" [checked]="showAll()" (change)="showAll.set(!showAll())" />
        <ng-container i18n="@@changes.showAll">Also answered and mine</ng-container>
      </label>
    </ui-page-header>

    @if (changes.isLoading() && !changes.value()) {
      <div class="flex justify-center py-16"><ui-spinner /></div>
    } @else if (!changes.value()?.length) {
      <ui-card><ui-empty-state i18n-message="@@changes.empty" message="Nothing waiting for you." /></ui-card>
    } @else {
      <div class="grid gap-4">
        @for (change of changes.value(); track change.id) {
          <ui-card>
            <div class="flex flex-wrap items-center gap-2">
              <ui-badge tone="primary">{{ kindLabel(change.operationKind) }}</ui-badge>
              <span class="font-medium">{{ actionLabel(change.kind) }}</span>
              <ui-badge [tone]="change.status === 'Failed' ? 'danger' : change.status === 'Applied' ? 'success' : 'warning'">
                {{ statusLabel(change.status) }}
              </ui-badge>
              @if (change.disputed) {
                <ui-badge tone="danger" i18n="@@operations.disputed">Disputed</ui-badge>
              }
              <span class="ml-auto text-xs text-muted">{{ change.createdAt | date: 'medium' }}</span>
            </div>

            <p class="mt-2 text-sm">
              @if (change.requestedByMe) {
                <span class="text-muted" i18n="@@changes.byMe">You asked:</span>
              } @else {
                <span class="text-muted" i18n="@@changes.byOther">{{ change.requestedByName // i18n(ph="name") }} asks:</span>
              }
              “{{ change.reason }}”
            </p>

            <div class="mt-3 grid gap-3 sm:grid-cols-2">
              <ng-container [ngTemplateOutlet]="snapshot" [ngTemplateOutletContext]="{ $implicit: change.current, title: nowLabel }" />
              @if (change.proposed) {
                <ng-container [ngTemplateOutlet]="snapshot" [ngTemplateOutletContext]="{ $implicit: change.proposed, title: proposedLabel }" />
              } @else {
                <div class="rounded-ui border border-danger p-3 text-sm text-danger" i18n="@@changes.willBeDeleted">It will be deleted (it stays in the history).</div>
              }
            </div>

            @if (change.approvals.length) {
              <ul class="mt-3 grid gap-1 text-xs text-muted">
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

            <div class="mt-4 flex flex-wrap justify-end gap-2">
              <button uiButton variant="ghost" size="sm" type="button" (click)="operations.history(change.operationId)">
                <ui-icon name="clock" size="sm" /><ng-container i18n="@@history.title">History</ng-container>
              </button>
              @if (change.status === 'Pending' && change.myApproval === 'Pending') {
                <button uiButton variant="secondary" size="sm" type="button" [loading]="busy() === change.id" (click)="reject(change)" i18n="@@changes.reject">Reject</button>
                <button uiButton size="sm" type="button" [loading]="busy() === change.id" (click)="accept(change)">
                  <ui-icon name="check" size="sm" /><ng-container i18n="@@changes.accept">Accept</ng-container>
                </button>
              }
            </div>
          </ui-card>
        }
      </div>
    }

    <ng-template #snapshot let-s let-title="title">
      <div class="rounded-ui border border-border p-3 text-sm">
        <p class="mb-1 text-xs font-semibold uppercase text-muted">{{ title }}</p>
        <p class="font-medium tabular-nums">{{ s.total | money: s.currency }}</p>
        <p class="text-muted">{{ s.occurredAt | date: 'medium' }}</p>
        @if (s.description) {
          <p>{{ s.description }}</p>
        }
        @if (!s.shares.length && s.fromName) {
          <p class="text-muted">{{ s.fromName }} → {{ s.toName }}</p>
        }
        @for (share of s.shares; track share.personId) {
          <p class="text-xs text-muted">{{ share.name }}: {{ share.amount | money: s.currency }}</p>
        }
      </div>
    </ng-template>
  `,
})
export class ChangesPage {
  private readonly api = inject(LedgerApi);
  private readonly dataChanges = inject(DataChanges);
  private readonly tracker = inject(OperationTracker);
  private readonly confirm = inject(ConfirmService);
  private readonly toasts = inject(ToastService);
  protected readonly operations = inject(OperationActions);

  protected readonly showAll = signal(false);
  protected readonly busy = signal<string | null>(null);
  protected readonly kindLabel = operationKindLabel;
  protected readonly actionLabel = changeActionLabel;
  protected readonly statusLabel = changeStatusLabel;
  protected readonly approvalLabel = approvalStatusLabel;
  protected readonly nowLabel = $localize`:@@changes.now:Now`;
  protected readonly proposedLabel = $localize`:@@changes.proposed:Proposed`;

  protected readonly changes = rxResource({
    params: () => ({ all: this.showAll(), version: this.dataChanges.version() }),
    stream: ({ params }) => this.api.changes(params.all ? 'All' : 'ToReview'),
  });

  protected async accept(change: OperationChange): Promise<void> {
    await this.respond(change, true, null);
  }

  protected async reject(change: OperationChange): Promise<void> {
    const reason = await this.confirm.askReason({
      title: $localize`:@@changes.rejectTitle:Reject the change?`,
      message: $localize`:@@changes.rejectMessage:Your books keep the current version; the others may apply it to theirs. It will be marked as disputed.`,
      confirmLabel: $localize`:@@changes.reject:Reject`,
      required: false,
      label: $localize`:@@changes.rejectReason:Why? (optional)`,
      danger: true,
    });

    if (reason !== null) await this.respond(change, false, reason);
  }

  private async respond(change: OperationChange, accept: boolean, reason: string | null): Promise<void> {
    this.busy.set(change.id);

    try {
      const result = await firstValueFrom(this.api.respondToChange(change.id, accept, reason));

      if (result.status === 'Pending') {
        // Others still have to answer: nothing changes in anybody's books yet.
        this.toasts.success($localize`:@@changes.answeredWaiting:Answer recorded. Still waiting for ${result.waitingFor.join(', ')}:names:.`);
        this.dataChanges.notify();
      } else {
        this.tracker.trackChange(result, $localize`:@@changes.answered:Thanks, your answer was recorded.`);
      }
    } finally {
      this.busy.set(null);
    }
  }
}

