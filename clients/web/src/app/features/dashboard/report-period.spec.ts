import { currentPeriod, periodRange, shiftPeriod } from './report-period';

describe('report period', () => {
  const today = new Date(2026, 8, 30, 15, 0); // 30 Sep 2026

  it('covers the whole current month by default', () => {
    const range = periodRange(currentPeriod('month', today));

    expect(range.from).toEqual(new Date(2026, 8, 1));
    expect(range.to).toEqual(new Date(2026, 8, 30, 23, 59, 59, 999));
  });

  it('covers the whole year', () => {
    const range = periodRange(currentPeriod('year', today));

    expect(range.from).toEqual(new Date(2026, 0, 1));
    expect(range.to).toEqual(new Date(2026, 11, 31, 23, 59, 59, 999));
  });

  it('moves to the previous month and year', () => {
    expect(periodRange(shiftPeriod(currentPeriod('month', today), -1)).from).toEqual(new Date(2026, 7, 1));
    expect(periodRange(shiftPeriod(currentPeriod('year', today), 1)).from).toEqual(new Date(2027, 0, 1));
  });

  it('uses inclusive days for custom ranges', () => {
    const range = periodRange({ ...currentPeriod('custom', today), customFrom: '2026-02-10', customTo: '2026-02-12' });

    expect(range.from).toEqual(new Date(2026, 1, 10));
    expect(range.to).toEqual(new Date(2026, 1, 12, 23, 59, 59, 999));
  });
});
