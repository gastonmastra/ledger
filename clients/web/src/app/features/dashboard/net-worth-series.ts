import { InvestmentsBucket, ReportBucket } from '../../core/api/models';

/** The lines of the "Net worth over time" chart: each part on its own, and their sum. */
export interface NetWorthSeries {
  /** Money in wallets (pending settlements included). */
  wallets: number[];
  owedToMe: number[];
  /** What I owe, as negative values (it lowers the net worth). */
  iOwe: number[];
  /** Market value of the investments; null when there are none in the period. */
  investments: number[] | null;
  /** wallets + owed to me − I owe + investments. */
  total: number[];
}

/**
 * Splits the net worth of every bucket into its parts. The investments buckets must be the same periods as
 * the report's (same request); otherwise they are left out.
 */
export function netWorthSeries(buckets: ReportBucket[], investments: InvestmentsBucket[] | null): NetWorthSeries {
  const aligned = investments && investments.length === buckets.length ? investments : null;
  const value = (i: number) => aligned?.[i].value ?? 0;

  return {
    wallets: buckets.map((b) => b.wallets),
    owedToMe: buckets.map((b) => b.owedToMe),
    iOwe: buckets.map((b) => (b.iOwe === 0 ? 0 : -b.iOwe)),
    investments: aligned ? aligned.map((b) => b.value) : null,
    total: buckets.map((b, i) => b.wallets + b.owedToMe - b.iOwe + value(i)),
  };
}
