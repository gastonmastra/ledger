import { formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, LOCALE_ID, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { LedgerApi } from '../../core/api/ledger-api';
import { Currency, InvestmentsReport, Report } from '../../core/api/models';
import { DataChanges } from '../../core/data-changes';
import { currentTimeZone } from '../../core/dates';
import { accountLabel } from '../../core/i18n/labels';
import { netWorthSeries } from './net-worth-series';
import {
  Button,
  Card,
  ChartConfigFactory,
  ChartView,
  EmptyState,
  Input,
  MoneyPipe,
  Spinner,
  Stat,
} from '../../ui';
import { amountTone } from '../shared/amount';
import { Period, PeriodMode, currentPeriod, periodRange, shiftPeriod } from './report-period';

/** Charts of the dashboard for a period (month, year or custom range) and a currency. */
@Component({
  selector: 'app-report-panel',
  imports: [FormsModule, Card, Button, Input, Stat, EmptyState, Spinner, ChartView, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './report-panel.html',
})
export class ReportPanel {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);
  private readonly locale = inject(LOCALE_ID);

  protected readonly period = signal<Period>(currentPeriod());
  protected readonly currency = signal<Currency | null>(null);
  protected readonly amountTone = amountTone;

  protected readonly modes: { value: PeriodMode; label: string }[] = [
    { value: 'month', label: $localize`:@@report.month:Month` },
    { value: 'year', label: $localize`:@@report.year:Year` },
    { value: 'custom', label: $localize`:@@report.custom:Custom` },
  ];

  protected readonly report = rxResource({
    params: () => ({ range: periodRange(this.period()), currency: this.currency(), version: this.changes.version() }),
    stream: ({ params }) =>
      this.api.report(params.range.from.toISOString(), params.range.to.toISOString(), params.currency, currentTimeZone()),
  });

  /** Value of the investments in the same buckets and currency as the ledger report. */
  protected readonly investments = rxResource({
    params: () => {
      const report = this.report.value();
      return report
        ? { range: periodRange(this.period()), currency: report.currency, version: this.changes.version() }
        : undefined;
    },
    stream: ({ params }) =>
      this.api.investmentsReport(params.range.from.toISOString(), params.range.to.toISOString(), params.currency, currentTimeZone()),
  });

  /** Investments of the report's currency, when there are any in the period. */
  protected readonly investmentsInPeriod = computed(() => {
    const report = this.report.value();
    const investments = this.investments.value();

    return report && investments?.currency === report.currency && investments.buckets.length === report.buckets.length
      && investments.buckets.some((b) => b.value !== 0 || b.cost !== 0)
      ? investments
      : null;
  });

  protected readonly title = computed(() => {
    const period = this.period();
    if (period.mode === 'month') return formatDate(period.anchor, 'MMMM y', this.locale);
    if (period.mode === 'year') return formatDate(period.anchor, 'y', this.locale);

    const range = periodRange(period);
    return `${formatDate(range.from, 'mediumDate', this.locale)} – ${formatDate(range.to, 'mediumDate', this.locale)}`;
  });

  protected readonly balance = computed(() => {
    const report = this.report.value();
    return report ? report.totalIncome - report.totalExpenses : 0;
  });

  protected readonly spendingChart = computed(() => this.buildSpendingChart(this.report.value()));
  protected readonly incomeExpensesChart = computed(() => this.buildIncomeExpensesChart(this.report.value()));
  protected readonly netWorthChart = computed(() =>
    this.buildNetWorthChart(this.report.value(), this.investmentsInPeriod()),
  );
  protected readonly investmentsChart = computed(() => this.buildInvestmentsChart(this.investmentsInPeriod()));
  protected readonly walletsChart = computed(() => this.buildWalletsChart(this.report.value()));

  protected setMode(mode: PeriodMode): void {
    this.period.update((p) => ({ ...p, mode, anchor: new Date() }));
  }

  protected shift(step: -1 | 1): void {
    this.period.update((p) => shiftPeriod(p, step));
  }

  protected setCustom(key: 'customFrom' | 'customTo', value: string): void {
    if (value) this.period.update((p) => ({ ...p, [key]: value }));
  }

  // ─── Charts ────────────────────────────────────────────────────────────────

  private money(currency: string): (value: number) => string {
    const format = new Intl.NumberFormat(this.locale, { style: 'currency', currency });
    return (value) => format.format(value);
  }

  private bucketLabels(report: Report): string[] {
    const pattern = report.granularity === 'Day' ? 'd MMM' : 'MMM y';
    return report.buckets.map((b) => formatDate(b.start, pattern, this.locale));
  }

  private buildSpendingChart(report: Report | undefined): ChartConfigFactory | null {
    if (!report?.spendingByCategory.length) return null;
    const money = this.money(report.currency);

    return (colors) => ({
      type: 'doughnut',
      data: {
        labels: report.spendingByCategory.map((c) => accountLabel({ accountName: c.name, accountPurpose: c.purpose })),
        datasets: [
          {
            data: report.spendingByCategory.map((c) => c.amount),
            backgroundColor: report.spendingByCategory.map((_, i) => colors.series[i % colors.series.length]),
            borderWidth: 0,
          },
        ],
      },
      options: {
        maintainAspectRatio: false,
        cutout: '62%',
        plugins: {
          legend: { position: 'bottom', labels: { boxWidth: 12, usePointStyle: true } },
          tooltip: { callbacks: { label: (ctx) => ` ${ctx.label}: ${money(ctx.parsed as number)}` } },
        },
      },
    });
  }

  private buildIncomeExpensesChart(report: Report | undefined): ChartConfigFactory | null {
    if (!report || (report.totalIncome === 0 && report.totalExpenses === 0)) return null;
    const money = this.money(report.currency);

    return (colors) => ({
      type: 'bar',
      data: {
        labels: this.bucketLabels(report),
        datasets: [
          {
            label: $localize`:@@report.income:Income`,
            data: report.buckets.map((b) => b.income),
            backgroundColor: colors.income,
            borderRadius: 4,
          },
          {
            label: $localize`:@@report.expenses:Expenses`,
            data: report.buckets.map((b) => b.expenses),
            backgroundColor: colors.expenses,
            borderRadius: 4,
          },
        ],
      },
      options: {
        maintainAspectRatio: false,
        scales: {
          x: { grid: { display: false } },
          y: { grid: { color: colors.grid }, ticks: { callback: (v) => money(Number(v)) } },
        },
        plugins: {
          legend: { position: 'bottom', labels: { boxWidth: 12, usePointStyle: true } },
          tooltip: { callbacks: { label: (ctx) => ` ${ctx.dataset.label}: ${money(ctx.parsed.y ?? 0)}` } },
        },
      },
    });
  }

  /**
   * Net worth over time: every part on its own line (wallets, owed to me, I owe as negative, investments) and,
   * drawn on top, their sum.
   */
  private buildNetWorthChart(report: Report | undefined, investments: InvestmentsReport | null): ChartConfigFactory | null {
    if (!report?.buckets.length) return null;
    const money = this.money(report.currency);
    const pointRadius = report.buckets.length > 31 ? 0 : 2;
    const series = netWorthSeries(report.buckets, investments?.buckets ?? null);

    const part = (label: string, data: number[], color: string) => ({
      label,
      data,
      borderColor: color,
      backgroundColor: color,
      borderWidth: 1.5,
      fill: false,
      tension: 0.3,
      pointRadius: 0,
      order: 1,
    });

    return (colors) => ({
      type: 'line',
      data: {
        labels: this.bucketLabels(report),
        datasets: [
          // order 0: drawn above the parts.
          {
            label: $localize`:@@report.totalNetWorth:Total net worth`,
            data: series.total,
            borderColor: colors.line,
            backgroundColor: colors.line,
            borderWidth: 3,
            // No area: it would hide the parts below it.
            fill: false,
            tension: 0.3,
            pointRadius,
            order: 0,
          },
          // series[0] is the primary color (the total's): the parts use other palette colors.
          part($localize`:@@report.wallets:Wallets`, series.wallets, colors.series[4]),
          part($localize`:@@account.receivables:Owed to me`, series.owedToMe, colors.income),
          part($localize`:@@debts.iOwe:I owe`, series.iOwe, colors.expenses),
          ...(series.investments ? [part($localize`:@@summary.investments:Investments`, series.investments, colors.series[2])] : []),
        ],
      },
      options: {
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        scales: {
          x: { grid: { display: false } },
          y: {
            grid: { color: (ctx) => (ctx.tick.value === 0 ? colors.text : colors.grid) },
            ticks: { callback: (v) => money(Number(v)) },
          },
        },
        plugins: {
          legend: { position: 'bottom', labels: { boxWidth: 12, usePointStyle: true } },
          tooltip: {
            // The parts first, then the total as the footer.
            filter: (item) => item.datasetIndex !== 0,
            callbacks: {
              label: (ctx) => ` ${ctx.dataset.label}: ${money(ctx.parsed.y ?? 0)}`,
              footer: (items) =>
                $localize`:@@report.totalNetWorth:Total net worth` + `: ${money(series.total[items[0]?.dataIndex ?? 0])}`,
            },
          },
        },
      },
    });
  }

  /** Market value of the investments against what they cost. */
  private buildInvestmentsChart(investments: InvestmentsReport | null): ChartConfigFactory | null {
    if (!investments) return null;
    const money = this.money(investments.currency);
    const pattern = investments.granularity === 'Day' ? 'd MMM' : 'MMM y';
    const pointRadius = investments.buckets.length > 31 ? 0 : 2;

    return (colors) => ({
      type: 'line',
      data: {
        labels: investments.buckets.map((b) => formatDate(b.start, pattern, this.locale)),
        datasets: [
          {
            label: $localize`:@@investments.value:Market value`,
            data: investments.buckets.map((b) => b.value),
            borderColor: colors.series[1],
            backgroundColor: colors.series[1] + '33',
            fill: true,
            tension: 0.3,
            pointRadius,
          },
          {
            label: $localize`:@@investments.cost:Cost`,
            data: investments.buckets.map((b) => b.cost),
            borderColor: colors.text,
            borderDash: [6, 4],
            fill: false,
            tension: 0,
            pointRadius: 0,
          },
        ],
      },
      options: {
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        scales: {
          x: { grid: { display: false } },
          y: { grid: { color: colors.grid }, ticks: { callback: (v) => money(Number(v)) } },
        },
        plugins: {
          legend: { position: 'bottom', labels: { boxWidth: 12, usePointStyle: true } },
          tooltip: { callbacks: { label: (ctx) => ` ${ctx.dataset.label}: ${money(ctx.parsed.y ?? 0)}` } },
        },
      },
    });
  }

  private buildWalletsChart(report: Report | undefined): ChartConfigFactory | null {
    if (!report?.walletBalances.length) return null;
    const money = this.money(report.currency);

    return (colors) => ({
      type: 'bar',
      data: {
        labels: report.walletBalances.map((w) => w.name),
        datasets: [
          {
            label: $localize`:@@report.balance:Balance`,
            data: report.walletBalances.map((w) => w.balance),
            backgroundColor: report.walletBalances.map((_, i) => colors.series[i % colors.series.length]),
            borderRadius: 4,
          },
        ],
      },
      options: {
        indexAxis: 'y',
        maintainAspectRatio: false,
        scales: {
          x: { grid: { color: colors.grid }, ticks: { callback: (v) => money(Number(v)) } },
          y: { grid: { display: false } },
        },
        plugins: {
          legend: { display: false },
          tooltip: { callbacks: { label: (ctx) => ` ${money(ctx.parsed.x ?? 0)}` } },
        },
      },
    });
  }
}
