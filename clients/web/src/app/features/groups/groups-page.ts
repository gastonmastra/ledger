import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { LedgerApi } from '../../core/api/ledger-api';
import { Group } from '../../core/api/models';
import { DataChanges } from '../../core/data-changes';
import { Button, Card, DialogService, EmptyState, Icon, PageHeader, Spinner } from '../../ui';
import { GroupDialog } from './group-dialogs';

@Component({
  selector: 'app-groups-page',
  imports: [RouterLink, PageHeader, Card, Button, Icon, EmptyState, Spinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-page-header i18n-heading="@@groups.title" heading="Groups"
                    i18n-subtitle="@@groups.subtitle" subtitle="Share expenses with several people and settle up with the fewest payments.">
      <button uiButton type="button" (click)="create()">
        <ui-icon name="plus" size="sm" /><ng-container i18n="@@groups.new">New group</ng-container>
      </button>
    </ui-page-header>

    @if (groups.isLoading() && !groups.value()) {
      <div class="flex justify-center py-16"><ui-spinner /></div>
    } @else if (!groups.value()?.length) {
      <ui-card><ui-empty-state i18n-message="@@groups.empty" message="No groups yet." /></ui-card>
    } @else {
      <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        @for (group of groups.value(); track group.id) {
          <a [routerLink]="['/groups', group.id]" class="block rounded-ui transition hover:ring-2 hover:ring-ring/40">
            <ui-card>
              <h2 class="font-semibold">{{ group.name }}</h2>
              <p class="mt-1 text-sm text-muted" i18n="@@groups.memberCount">{{ group.members.length }} members</p>
            </ui-card>
          </a>
        }
      </div>
    }
  `,
})
export class GroupsPage {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);
  private readonly dialogs = inject(DialogService);
  private readonly router = inject(Router);

  protected readonly groups = rxResource({
    params: () => this.changes.version(),
    stream: () => this.api.groups(),
  });

  protected async create(): Promise<void> {
    const group = await this.dialogs.open<Group>(GroupDialog);

    if (group) {
      this.changes.notify();
      await this.router.navigate(['/groups', group.id]);
    }
  }
}
