import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { appSettings } from '../../app.settings';
import { LedgerApi } from '../../core/api/ledger-api';
import { LinkRequest } from '../../core/api/models';
import { DataChanges } from '../../core/data-changes';
import { linkRequestStatusLabel } from '../../core/i18n/labels';
import { ToastService } from '../../core/notifications/toast.service';
import { Badge, Button, Card, ConfirmService, Icon, MoneyPipe, Spinner } from '../../ui';
import { amountClass } from '../shared/amount';

/** One incoming request: who asks, what it copies into my books, and Accept / Reject. */
@Component({
  selector: 'app-incoming-link-request',
  imports: [DatePipe, Card, Badge, Button, Icon, Spinner, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let req = request();
    <ui-card>
      <div class="flex flex-wrap items-center gap-2">
        <ui-badge tone="primary" i18n="@@links.badge">Link request</ui-badge>
        <ui-badge [tone]="req.status === 'Failed' ? 'danger' : req.status === 'Applied' ? 'success' : 'warning'">{{ statusLabel(req.status) }}</ui-badge>
        <span class="ml-auto text-xs text-muted">{{ req.createdAt | date: 'medium' }}</span>
      </div>
      <p class="mt-2 text-sm" i18n="@@links.incoming">
        {{ req.ownerName // i18n(ph="owner") }} had you as the contact "{{ req.contactName // i18n(ph="contact") }}" before you joined
        and wants to link it to your account.
      </p>

      @if (req.status === 'Pending') {
        @if (preview.isLoading() && !preview.value()) {
          <div class="flex justify-center py-4"><ui-spinner /></div>
        } @else if (preview.value(); as data) {
          <div class="mt-3 rounded-ui border border-border p-3 text-sm">
            <p class="mb-2 text-xs font-semibold uppercase text-muted" i18n="@@links.youWillSee">If you accept, your books get</p>
            @for (balance of data.balances; track balance.counterpartyId + balance.currency) {
              <p class="flex justify-between gap-3">
                <span>{{ balance.name }}</span>
                <span class="tabular-nums" [class]="amountClass(balance.amount)">
                  @if (balance.amount < 0) {
                    <ng-container i18n="@@links.youOwe">you owe {{ -balance.amount | money: balance.currency }}</ng-container>
                  } @else {
                    <ng-container i18n="@@links.owesYou">owes you {{ balance.amount | money: balance.currency }}</ng-container>
                  }
                </span>
              </p>
            } @empty {
              <p class="text-muted" i18n="@@links.nothingOpen">Nothing open: you are even.</p>
            }
            <p class="mt-2 text-xs text-muted" i18n="@@links.copied">
              {{ data.operations // i18n(ph="count") }} operation(s) copied as existing debts (neither income nor expense), with their original dates.
              From then on you share every change with {{ req.ownerName // i18n(ph="owner") }}.
            </p>
          </div>
        }

        <div class="mt-4 flex flex-wrap justify-end gap-2">
          <button uiButton variant="secondary" size="sm" type="button" [loading]="busy()" (click)="reject()" i18n="@@changes.reject">Reject</button>
          <button uiButton size="sm" type="button" [loading]="busy()" (click)="accept()">
            <ui-icon name="check" size="sm" /><ng-container i18n="@@changes.accept">Accept</ng-container>
          </button>
        </div>
      } @else if (req.status === 'Failed' && req.failureReason) {
        <p class="mt-2 text-sm text-danger">{{ req.failureReason }}</p>
      }
    </ui-card>
  `,
})
export class IncomingLinkRequest {
  private readonly api = inject(LedgerApi);
  private readonly dataChanges = inject(DataChanges);
  private readonly toasts = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly request = input.required<LinkRequest>();

  protected readonly busy = signal(false);
  protected readonly statusLabel = linkRequestStatusLabel;
  protected readonly amountClass = amountClass;

  protected readonly preview = rxResource({
    params: () => (this.request().status === 'Pending' ? this.request().id : undefined),
    stream: ({ params }) => this.api.linkPreview(params),
  });

  protected async accept(): Promise<void> {
    this.busy.set(true);

    try {
      await firstValueFrom(this.api.acceptLinkRequest(this.request().id));
      this.toasts.info($localize`:@@operation.processing:Processing… we'll let you know when it's recorded.`);
      this.dataChanges.notify();
      void this.waitApplied(this.request().id);
    } finally {
      this.busy.set(false);
    }
  }

  protected async reject(): Promise<void> {
    const confirmed = await this.confirm.confirm({
      title: $localize`:@@links.rejectTitle:Reject the link?`,
      message: $localize`:@@links.rejectMessage:Nothing is copied into your books and the contact stays unlinked.`,
      confirmLabel: $localize`:@@changes.reject:Reject`,
      danger: true,
    });

    if (!confirmed) return;

    this.busy.set(true);

    try {
      await firstValueFrom(this.api.rejectLinkRequest(this.request().id));
      this.dataChanges.notify();
    } finally {
      this.busy.set(false);
    }
  }

  /** The merge runs in the background: tell the result when it is done. */
  private async waitApplied(id: string): Promise<void> {
    const deadline = Date.now() + appSettings.operationTimeoutMs;

    while (Date.now() < deadline) {
      await new Promise((resolve) => setTimeout(resolve, appSettings.operationPollMs));

      try {
        const request = (await firstValueFrom(this.api.linkRequests(true))).find((r) => r.id === id);

        if (request?.status === 'Applied') {
          this.toasts.success($localize`:@@links.applied:Linked. The shared history is now in your books.`);
          this.dataChanges.notify();
          return;
        }

        if (request?.status === 'Failed') {
          this.toasts.error($localize`:@@links.failed:The link could not be applied: ${request.failureReason}:reason:`);
          this.dataChanges.notify();
          return;
        }
      } catch {
        // Transient error: keep trying until the deadline.
      }
    }

    this.toasts.info($localize`:@@operation.slow:It is taking longer than usual. Check again in a moment.`);
  }
}

/** Link requests: the ones I have to answer, and the ones I sent. */
@Component({
  selector: 'app-link-requests',
  imports: [DatePipe, Card, Badge, IncomingLinkRequest],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (incoming().length || outgoing().length) {
      <div class="mb-6 grid gap-4">
        @for (request of incoming(); track request.id) {
          <app-incoming-link-request [request]="request" />
        }
        @for (request of outgoing(); track request.id) {
          <ui-card>
            <div class="flex flex-wrap items-center gap-2">
              <ui-badge tone="primary" i18n="@@links.badge">Link request</ui-badge>
              <ui-badge [tone]="request.status === 'Failed' ? 'danger' : request.status === 'Applied' ? 'success' : 'warning'">{{ statusLabel(request.status) }}</ui-badge>
              <span class="ml-auto text-xs text-muted">{{ request.createdAt | date: 'medium' }}</span>
            </div>
            <p class="mt-2 text-sm" i18n="@@links.outgoing">
              You asked {{ request.targetName // i18n(ph="user") }} to link your contact "{{ request.contactName // i18n(ph="contact") }}" to their account.
            </p>
            @if (request.status === 'Failed' && request.failureReason) {
              <p class="mt-1 text-sm text-danger">{{ request.failureReason }}</p>
            }
          </ui-card>
        }
      </div>
    }
  `,
})
export class LinkRequests {
  private readonly api = inject(LedgerApi);
  private readonly dataChanges = inject(DataChanges);

  /** Also answered requests, and the ones I sent. */
  readonly showAll = input(false);

  protected readonly statusLabel = linkRequestStatusLabel;

  private readonly requests = rxResource({
    params: () => ({ all: this.showAll(), version: this.dataChanges.version() }),
    stream: ({ params }) => this.api.linkRequests(params.all),
  });

  protected readonly incoming = computed(() => (this.requests.value() ?? []).filter((r) => r.direction === 'Incoming'));
  protected readonly outgoing = computed(() =>
    (this.requests.value() ?? []).filter((r) => r.direction === 'Outgoing' && (this.showAll() || r.status === 'Applying')),
  );
}
