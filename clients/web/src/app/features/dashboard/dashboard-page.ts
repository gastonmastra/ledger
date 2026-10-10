import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { LedgerApi } from '../../core/api/ledger-api';
import { DataChanges } from '../../core/data-changes';
import { Badge, Button, Card, EmptyState, MoneyPipe, PageHeader, Spinner, Stat, Table } from '../../ui';
import { Currency, CurrencySummary, InvestmentsTotal } from '../../core/api/models';
import { amountClass, amountTone } from '../shared/amount';
import { ReportPanel } from './report-panel';

@Component({
  selector: 'app-dashboard-page',
  imports: [RouterLink, PageHeader, Card, Stat, Table, Badge, Button, EmptyState, Spinner, MoneyPipe, ReportPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard-page.html',
})
export class DashboardPage {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);

  protected readonly summary = rxResource({
    params: () => this.changes.version(),
    stream: () => this.api.summary(),
  });

  protected readonly investments = rxResource({
    params: () => this.changes.version(),
    stream: () => this.api.investmentsSummary(),
  });

  protected readonly holdings = rxResource({
    params: () => this.changes.version(),
    stream: () => this.api.assets(),
  });

  /** Per currency: the money in the books plus the market value of the investments. */
  protected readonly cards = computed<DashboardCard[]>(() => {
    const totals = this.summary.value()?.totals ?? [];
    const investments = this.investments.value() ?? [];
    const currencies = [...new Set<Currency>([...totals.map((t) => t.currency), ...investments.map((i) => i.currency)])].sort();

    return currencies.map((currency) => {
      const money = totals.find((t) => t.currency === currency) ?? emptyTotal(currency);
      const invested = investments.find((i) => i.currency === currency) ?? null;
      return { currency, money, investments: invested, total: money.net + (invested?.value ?? 0) };
    });
  });

  protected readonly heldAssets = computed(() => (this.holdings.value() ?? []).filter((a) => a.quantity > 0));

  protected readonly pending = rxResource({
    params: () => this.changes.version(),
    stream: () => this.api.pendingAssignments(),
  });

  protected readonly hasData = computed(() => {
    const summary = this.summary.value();
    return (
      (!!summary && (summary.totals.length > 0 || summary.wallets.length > 0)) || !!this.investments.value()?.length
    );
  });

  protected readonly amountTone = amountTone;
  protected readonly amountClass = amountClass;
}

interface DashboardCard {
  currency: Currency;
  money: CurrencySummary;
  investments: InvestmentsTotal | null;
  /** Money net + investments at market value. */
  total: number;
}

function emptyTotal(currency: Currency): CurrencySummary {
  return { currency, wallets: 0, owedToMe: 0, iOwe: 0, pending: 0, net: 0, adjustments: 0 };
}
