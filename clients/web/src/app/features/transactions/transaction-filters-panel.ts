import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, Signal, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Category, Contact, Wallet } from '../../core/api/models';
import { Button, DialogLayout, FormField, Input } from '../../ui';

export interface TransactionFilters {
  walletId: string;
  categoryId: string;
  contactId: string;
  from: string;
  to: string;
}

/** The page's filter state: the panel changes it directly, so the list updates while it is open. */
export interface TransactionFiltersData {
  filters: Signal<TransactionFilters>;
  includeHistory: Signal<boolean>;
  wallets: Wallet[];
  categories: Category[];
  contacts: Contact[];
  set: (key: keyof TransactionFilters, value: string) => void;
  toggleHistory: () => void;
  clear: () => void;
}

/** Filters of the transactions list, in a side panel (desktop) or a bottom sheet (phones). */
@Component({
  selector: 'app-transaction-filters-panel',
  imports: [FormsModule, DialogLayout, FormField, Input, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog i18n-heading="@@transactions.filters" heading="Filters">
      <div class="grid gap-4">
        <ui-form-field i18n-label="@@transactions.filterWallet" label="Wallet">
          <select uiInput [ngModel]="data.filters().walletId" (ngModelChange)="data.set('walletId', $event)">
            <option value="" i18n="@@ui.all">All</option>
            @for (wallet of data.wallets; track wallet.id) {
              <option [value]="wallet.id">{{ wallet.name }}</option>
            }
          </select>
        </ui-form-field>
        <ui-form-field i18n-label="@@transactions.filterCategory" label="Category">
          <select uiInput [ngModel]="data.filters().categoryId" (ngModelChange)="data.set('categoryId', $event)">
            <option value="" i18n="@@ui.all">All</option>
            @for (category of data.categories; track category.id) {
              <option [value]="category.id">{{ category.name }}</option>
            }
          </select>
        </ui-form-field>
        <ui-form-field i18n-label="@@transactions.filterContact" label="Contact">
          <select uiInput [ngModel]="data.filters().contactId" (ngModelChange)="data.set('contactId', $event)">
            <option value="" i18n="@@ui.all">All</option>
            @for (contact of data.contacts; track contact.id) {
              <option [value]="contact.id">{{ contact.name }}</option>
            }
          </select>
        </ui-form-field>
        <div class="grid grid-cols-2 gap-3">
          <ui-form-field i18n-label="@@transactions.filterFrom" label="From">
            <input uiInput type="date" [ngModel]="data.filters().from" (ngModelChange)="data.set('from', $event)" />
          </ui-form-field>
          <ui-form-field i18n-label="@@transactions.filterTo" label="To">
            <input uiInput type="date" [ngModel]="data.filters().to" (ngModelChange)="data.set('to', $event)" />
          </ui-form-field>
        </div>
        <label class="flex cursor-pointer items-center gap-2 text-sm"
               i18n-title="@@transactions.showChangesHint" title="Also show replaced and deleted versions and the reversals that cancelled them.">
          <input type="checkbox" class="accent-primary" [checked]="data.includeHistory()" (change)="data.toggleHistory()" />
          <ng-container i18n="@@transactions.showChanges">Show changes</ng-container>
        </label>
      </div>
      <div dialog-actions class="contents">
        <button uiButton variant="secondary" type="button" (click)="data.clear()" i18n="@@transactions.clearFilters">Clear filters</button>
        <button uiButton type="button" (click)="ref.close()" i18n="@@ui.done">Done</button>
      </div>
    </ui-dialog>
  `,
})
export class TransactionFiltersPanel {
  protected readonly data = inject<TransactionFiltersData>(DIALOG_DATA);
  protected readonly ref = inject(DialogRef);
}
