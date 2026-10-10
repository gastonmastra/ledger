import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { Category } from '../../core/api/models';
import { DataChanges } from '../../core/data-changes';
import { categoryTypeLabel } from '../../core/i18n/labels';
import { ToastService } from '../../core/notifications/toast.service';
import { Badge, Button, Card, ConfirmService, DialogService, EmptyState, Icon, PageHeader, Spinner, Table } from '../../ui';
import { NameDialog, NameDialogData } from '../shared/name-dialog';
import { CategoryDialog } from './category-dialog';

@Component({
  selector: 'app-categories-page',
  imports: [PageHeader, Card, Table, Badge, Button, Icon, EmptyState, Spinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-page-header i18n-heading="@@categories.title" heading="Categories"
                    i18n-subtitle="@@categories.subtitle" subtitle="Group your income and expenses.">
      <button uiButton type="button" (click)="create()">
        <ui-icon name="plus" size="sm" /><ng-container i18n="@@categories.new">New category</ng-container>
      </button>
    </ui-page-header>

    <ui-card [padded]="false">
      @if (categories.isLoading() && !categories.value()) {
        <div class="flex justify-center py-10"><ui-spinner /></div>
      } @else if (!categories.value()?.length) {
        <ui-empty-state i18n-message="@@categories.empty" message="No categories yet. Without one, movements go to Uncategorized." />
      } @else {
        <ui-table>
          <thead>
            <tr>
              <th i18n="@@categories.name">Name</th>
              <th i18n="@@categories.type">Type</th>
              <th><span class="sr-only" i18n="@@ui.actions">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (category of categories.value(); track category.id) {
              <tr>
                <td class="font-medium">{{ category.name }}</td>
                <td><ui-badge [tone]="category.type === 'Income' ? 'success' : 'neutral'">{{ typeLabel(category.type) }}</ui-badge></td>
                <td class="text-right">
                  <button uiButton variant="ghost" size="sm" type="button" (click)="rename(category)" i18n="@@ui.rename">Rename</button>
                  <button uiButton variant="ghost" size="sm" type="button" (click)="archive(category)" i18n="@@categories.archive">Archive</button>
                </td>
              </tr>
            }
          </tbody>
        </ui-table>
      }
    </ui-card>
  `,
})
export class CategoriesPage {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);
  private readonly dialogs = inject(DialogService);
  private readonly confirm = inject(ConfirmService);
  private readonly toasts = inject(ToastService);

  protected readonly typeLabel = categoryTypeLabel;

  protected readonly categories = rxResource({
    params: () => this.changes.version(),
    stream: () => this.api.categories(),
  });

  protected async create(): Promise<void> {
    if (await this.dialogs.open<boolean>(CategoryDialog)) {
      this.toasts.success($localize`:@@categories.created:Category created.`);
      this.changes.notify();
    }
  }

  protected async rename(category: Category): Promise<void> {
    const name = await this.dialogs.open<string, NameDialogData>(NameDialog, {
      title: $localize`:@@categories.rename:Rename category`,
      label: $localize`:@@categories.name:Name`,
      value: category.name,
      maxLength: 200,
    });

    if (name && name !== category.name) {
      await firstValueFrom(this.api.renameCategory(category.id, name));
      this.changes.notify();
    }
  }

  protected async archive(category: Category): Promise<void> {
    const confirmed = await this.confirm.confirm({
      title: $localize`:@@categories.archiveTitle:Archive category`,
      message: $localize`:@@categories.archiveMessage:"${category.name}:name:" will no longer be offered. Past transactions keep it.`,
      confirmLabel: $localize`:@@categories.archive:Archive`,
    });

    if (confirmed) {
      await firstValueFrom(this.api.archiveCategory(category.id));
      this.changes.notify();
    }
  }
}
