import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { LedgerApi } from '../../core/api/ledger-api';
import { DataChanges } from '../../core/data-changes';
import { Badge, Button, Card, EmptyState, MoneyPipe, PageHeader, Spinner, Stat, Table } from '../../ui';
import { amountClass, amountTone } from '../shared/amount';

@Component({
  selector: 'app-dashboard-page',
  imports: [RouterLink, PageHeader, Card, Stat, Table, Badge, Button, EmptyState, Spinner, MoneyPipe],
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

  protected readonly pending = rxResource({
    params: () => this.changes.version(),
    stream: () => this.api.pendingAssignments(),
  });

  protected readonly hasData = computed(() => {
    const summary = this.summary.value();
    return !!summary && (summary.totals.length > 0 || summary.wallets.length > 0);
  });

  protected readonly amountTone = amountTone;
  protected readonly amountClass = amountClass;
}
