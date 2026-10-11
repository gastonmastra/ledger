import { InvestmentsBucket, ReportBucket } from '../../core/api/models';
import { netWorthSeries } from './net-worth-series';

const bucket = (wallets: number, owedToMe: number, iOwe: number): ReportBucket => ({
  start: '2026-10-01T00:00:00Z', income: 0, expenses: 0, netWorth: wallets + owedToMe - iOwe, wallets, owedToMe, iOwe,
});
const investment = (value: number): InvestmentsBucket => ({ start: '2026-10-01T00:00:00Z', value, cost: value });

describe('netWorthSeries', () => {
  it('keeps every part on its own, I owe as negative, and adds them up in the total', () => {
    const series = netWorthSeries([bucket(1000, 100, 30), bucket(900, 150, 0)], [investment(200), investment(250)]);

    expect(series.wallets).toEqual([1000, 900]);
    expect(series.owedToMe).toEqual([100, 150]);
    expect(series.iOwe).toEqual([-30, 0]);
    expect(series.investments).toEqual([200, 250]);
    expect(series.total).toEqual([1270, 1300]);
  });

  it('without investments the total is the net worth', () => {
    const buckets = [bucket(500, 0, 20)];
    const series = netWorthSeries(buckets, null);

    expect(series.investments).toBeNull();
    expect(series.total).toEqual([buckets[0].netWorth]);
  });

  it('leaves out investments of other periods', () => {
    const series = netWorthSeries([bucket(10, 0, 0), bucket(20, 0, 0)], [investment(5)]);

    expect(series.investments).toBeNull();
    expect(series.total).toEqual([10, 20]);
  });
});
