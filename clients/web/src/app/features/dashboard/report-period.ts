export type PeriodMode = 'month' | 'year' | 'custom';

export interface Period {
  mode: PeriodMode;
  /** Any date inside the month or year shown. */
  anchor: Date;
  /** Custom range, as yyyy-MM-dd (local days, inclusive). */
  customFrom: string;
  customTo: string;
}

export interface PeriodRange {
  from: Date;
  to: Date;
}

/** Start and end (inclusive) of the period, in the user's local time. */
export function periodRange(period: Period): PeriodRange {
  const { anchor } = period;

  switch (period.mode) {
    case 'month':
      return {
        from: new Date(anchor.getFullYear(), anchor.getMonth(), 1),
        to: new Date(anchor.getFullYear(), anchor.getMonth() + 1, 1, 0, 0, 0, -1),
      };
    case 'year':
      return {
        from: new Date(anchor.getFullYear(), 0, 1),
        to: new Date(anchor.getFullYear() + 1, 0, 1, 0, 0, 0, -1),
      };
    case 'custom': {
      const [fy, fm, fd] = period.customFrom.split('-').map(Number);
      const [ty, tm, td] = period.customTo.split('-').map(Number);
      return { from: new Date(fy, fm - 1, fd), to: new Date(ty, tm - 1, td + 1, 0, 0, 0, -1) };
    }
  }
}

/** Moves a month or year period backwards (-1) or forwards (+1). */
export function shiftPeriod(period: Period, step: -1 | 1): Period {
  const anchor = new Date(period.anchor);

  if (period.mode === 'month') anchor.setMonth(anchor.getMonth() + step, 1);
  if (period.mode === 'year') anchor.setFullYear(anchor.getFullYear() + step, 0, 1);

  return { ...period, anchor };
}

export function currentPeriod(mode: PeriodMode = 'month', today: Date = new Date()): Period {
  const first = new Date(today.getFullYear(), today.getMonth(), 1);
  const iso = (d: Date) =>
    `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

  return { mode, anchor: today, customFrom: iso(first), customTo: iso(today) };
}
