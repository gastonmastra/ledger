import { isInFuture, isoToLocalInput, localInputToIso, toLocalInputValue } from './dates';

describe('dates', () => {
  it('round-trips a local date-time through UTC', () => {
    const local = '2026-01-15T10:30';
    const iso = localInputToIso(local);

    expect(iso.endsWith('Z')).toBe(true);
    expect(isoToLocalInput(iso)).toBe(local);
  });

  it('formats dates for datetime-local inputs', () => {
    expect(toLocalInputValue(new Date(2026, 8, 5, 7, 3))).toBe('2026-09-05T07:03');
  });

  it('detects future dates, with a minute of tolerance', () => {
    const now = new Date(2026, 8, 30, 12, 0);

    expect(isInFuture('2026-09-30T12:00', now)).toBe(false);
    expect(isInFuture('2026-09-30T12:05', now)).toBe(true);
  });
});
