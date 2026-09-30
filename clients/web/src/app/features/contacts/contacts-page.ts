import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { LedgerApi } from '../../core/api/ledger-api';
import { Contact } from '../../core/api/models';
import { DataChanges } from '../../core/data-changes';
import { ToastService } from '../../core/notifications/toast.service';
import { Badge, Button, Card, DialogService, EmptyState, Icon, PageHeader, Spinner, Table } from '../../ui';
import { ContactDialog, LinkContactDialog } from './contact-dialogs';

@Component({
  selector: 'app-contacts-page',
  imports: [PageHeader, Card, Table, Badge, Button, Icon, EmptyState, Spinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-page-header i18n-heading="@@contacts.title" heading="Contacts"
                    i18n-subtitle="@@contacts.subtitle" subtitle="People you lend money to or share expenses with.">
      <button uiButton type="button" (click)="create()">
        <ui-icon name="plus" size="sm" /><ng-container i18n="@@contacts.new">New contact</ng-container>
      </button>
    </ui-page-header>

    <ui-card [padded]="false">
      @if (contacts.isLoading() && !contacts.value()) {
        <div class="flex justify-center py-10"><ui-spinner /></div>
      } @else if (!contacts.value()?.length) {
        <ui-empty-state i18n-message="@@contacts.empty" message="No contacts yet." />
      } @else {
        <ui-table>
          <thead>
            <tr>
              <th i18n="@@contacts.name">Name</th>
              <th i18n="@@contacts.email">Email</th>
              <th i18n="@@contacts.account">Ledger account</th>
              <th><span class="sr-only" i18n="@@ui.actions">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (contact of contacts.value(); track contact.id) {
              <tr>
                <td class="font-medium">
                  {{ contact.name }}
                  @if (contact.origin === 'Mirrored') {
                    <ui-badge class="ml-1" i18n="@@contacts.auto">added automatically</ui-badge>
                  }
                </td>
                <td class="text-muted">{{ contact.email }}</td>
                <td>
                  @if (contact.isLinked) {
                    <ui-badge tone="success" i18n="@@contacts.linked">Linked</ui-badge>
                  } @else {
                    <ui-badge i18n="@@contacts.notLinked">Not linked</ui-badge>
                  }
                </td>
                <td class="text-right">
                  @if (!contact.isLinked && !contact.isInUse) {
                    <button uiButton variant="ghost" size="sm" type="button" (click)="link(contact)" i18n="@@contacts.link">Link</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </ui-table>
      }
    </ui-card>
  `,
})
export class ContactsPage {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);
  private readonly dialogs = inject(DialogService);
  private readonly toasts = inject(ToastService);

  protected readonly contacts = rxResource({
    params: () => this.changes.version(),
    stream: () => this.api.contacts(),
  });

  protected async create(): Promise<void> {
    const contact = await this.dialogs.open<Contact>(ContactDialog);

    if (contact) {
      this.toasts.success(
        contact.isLinked
          ? $localize`:@@contacts.createdLinked:${contact.name}:name: added and linked to their Ledger account.`
          : $localize`:@@contacts.created:${contact.name}:name: added.`,
      );
      this.changes.notify();
    }
  }

  protected async link(contact: Contact): Promise<void> {
    if (await this.dialogs.open<boolean, Contact>(LinkContactDialog, contact)) {
      this.toasts.success($localize`:@@contacts.linkedSaved:${contact.name}:name: is now linked.`);
      this.changes.notify();
    }
  }
}
