import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { appSettings } from '../../app.settings';
import { LedgerApi } from '../api/ledger-api';
import { Operation } from '../api/models';
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
