import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { appSettings } from '../../app.settings';
import { LedgerApi } from '../api/ledger-api';
import { ChangeResult, Operation } from '../api/models';
import { DataChanges } from '../data-changes';
import { ToastService } from '../notifications/toast.service';

/**
 * Loans, splits and settlements are recorded asynchronously by the API.
 * "Fire and notify": the form closes right away, and a toast tells the result later.
 */
@Injectable({ providedIn: 'root' })
export class OperationTracker {
  private readonly api = inject(LedgerApi);
  private readonly toasts = inject(ToastService);
  private readonly changes = inject(DataChanges);

  /** Starts tracking an operation the API accepted. Does not wait for the outcome. */
  track(operation: Operation, successMessage: string): void {
    this.toasts.info($localize`:@@operation.processing:Processing… we'll let you know when it's recorded.`);
    void this.poll(operation, successMessage);
  }

  /** Submits the request, then tracks it. Rejects only if the API refused the request itself. */
  async submit(request: Observable<Operation>, successMessage: string): Promise<void> {
    this.track(await firstValueFrom(request), successMessage);
  }

  /**
   * Submits a change of a shared operation (edit, delete, restore, or an answer to a change).
   * Linked users may have to confirm it first; otherwise the books are updated asynchronously.
   */
  async submitChange(request: Observable<ChangeResult>, successMessage: string): Promise<void> {
    this.trackChange(await firstValueFrom(request), successMessage);
  }

  trackChange(result: ChangeResult, successMessage: string): void {
    this.changes.notify();

    if (result.status === 'Pending') {
      this.toasts.info(
        $localize`:@@change.waiting:Sent for confirmation to ${result.waitingFor.join(', ')}:names:. Nothing changes until they answer.`,
      );
      return;
    }

    void this.pollChange(result, successMessage);
  }

  private async pollChange(result: ChangeResult, successMessage: string): Promise<void> {
    const deadline = Date.now() + appSettings.operationTimeoutMs;
    let status = result.status;
    let failure: string | null = null;

    while (status === 'Applying' && Date.now() < deadline) {
      await delay(appSettings.operationPollMs);

      try {
        const history = await firstValueFrom(this.api.operationHistory(result.operationId));
        const change = history.changes.find((c) => c.id === result.changeId);
        status = change?.status ?? status;
        failure = change?.failureReason ?? null;
      } catch {
        // Transient error: keep trying until the deadline.
      }
    }

    switch (status) {
      case 'Applied':
        this.toasts.success(successMessage);
        this.changes.notify();
        break;
      case 'Failed':
        this.toasts.error($localize`:@@change.failed:The change could not be applied: ${failure}:reason:`);
        this.changes.notify();
        break;
      default:
        this.toasts.info($localize`:@@operation.slow:It is taking longer than usual. Check again in a moment.`);
    }
  }

  private async poll(operation: Operation, successMessage: string): Promise<void> {
    const deadline = Date.now() + appSettings.operationTimeoutMs;
    let current = operation;

    while (current.status === 'Pending' && Date.now() < deadline) {
      await delay(appSettings.operationPollMs);

      try {
        current = await firstValueFrom(this.api.operation(operation.id));
      } catch {
        // Transient error: keep trying until the deadline.
      }
    }

    switch (current.status) {
      case 'Completed':
        this.toasts.success(successMessage);
        this.changes.notify();
        break;
      case 'Rejected':
        this.toasts.error(
          $localize`:@@operation.rejected:It could not be recorded: ${current.rejectionReason}:reason:`,
        );
        this.changes.notify();
        break;
      default:
        this.toasts.info($localize`:@@operation.slow:It is taking longer than usual. Check again in a moment.`);
    }
  }
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
