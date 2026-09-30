import { TestBed } from '@angular/core/testing';
import { DataChanges, EVENTUAL_REFRESH_DELAY_MS } from './data-changes';

describe('DataChanges', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('refreshes now and once more after the other modules caught up', () => {
    const changes = TestBed.inject(DataChanges);

    changes.notify();
    expect(changes.version()).toBe(1);

    vi.advanceTimersByTime(EVENTUAL_REFRESH_DELAY_MS);
    expect(changes.version()).toBe(2);
  });

  it('coalesces the follow-up refresh of quick successive changes', () => {
    const changes = TestBed.inject(DataChanges);

    changes.notify();
    changes.notify();
    vi.advanceTimersByTime(EVENTUAL_REFRESH_DELAY_MS);

    expect(changes.version()).toBe(3);
  });
});
