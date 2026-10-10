import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { LedgerApi } from '../api/ledger-api';
import { Operation } from '../api/models';
import { DataChanges } from '../data-changes';
import { ToastService } from '../notifications/toast.service';
import { OperationTracker } from './operation-tracker';

const pending: Operation = {
  id: 'op-1',
  kind: 'Loan',
  status: 'Pending',
  rejectionReason: null,
  createdAt: '2026-09-30T10:00:00Z',
  completedAt: null,
};

describe('OperationTracker', () => {
  let statuses: Operation[];

  beforeEach(() => {
    vi.useFakeTimers();
    statuses = [];
    TestBed.configureTestingModule({
      providers: [{ provide: LedgerApi, useValue: { operation: () => of(statuses.shift() ?? pending) } }],
    });
  });

  afterEach(() => vi.useRealTimers());

  it('notifies success when the operation completes (fire and notify)', async () => {
    const tracker = TestBed.inject(OperationTracker);
    const toasts = TestBed.inject(ToastService);
    const changes = TestBed.inject(DataChanges);
    statuses = [pending, { ...pending, status: 'Completed' }];

    tracker.track(pending, 'Loan recorded.');
    expect(toasts.toasts().map((t) => t.tone)).toEqual(['info']);

    await vi.advanceTimersByTimeAsync(2_000);

    expect(toasts.toasts().some((t) => t.tone === 'success' && t.message === 'Loan recorded.')).toBe(true);
    expect(changes.version()).toBeGreaterThan(0);
  });

  it('shows the rejection reason', async () => {
    const tracker = TestBed.inject(OperationTracker);
    const toasts = TestBed.inject(ToastService);
    statuses = [{ ...pending, status: 'Rejected', rejectionReason: 'Not enough money.' }];

    tracker.track(pending, 'Loan recorded.');
    await vi.advanceTimersByTimeAsync(1_000);

    const error = toasts.toasts().find((t) => t.tone === 'error');
    expect(error?.message).toContain('Not enough money.');
  });
});
