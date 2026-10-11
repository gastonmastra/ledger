import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Contact } from '../../core/api/models';
import { Badge, Button, DialogLayout, Icon } from '../../ui';

export type ContactAction = 'link' | 'cancelLink';

/** A contact: email, whether they are linked to a Ledger account, and linking. */
@Component({
  selector: 'app-contact-detail-dialog',
  imports: [DialogLayout, Badge, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [heading]="contact.name">
      <div class="grid gap-3 text-sm">
        @if (contact.email) {
          <p class="text-muted">{{ contact.email }}</p>
        }
        <div class="flex flex-wrap gap-2">
          @if (contact.isLinked) {
            <ui-badge tone="success" i18n="@@contacts.linked">Linked</ui-badge>
          } @else if (contact.pendingLinkRequestId) {
            <ui-badge tone="warning" i18n="@@contacts.linkPending">Waiting for them to accept</ui-badge>
          } @else {
            <ui-badge i18n="@@contacts.notLinked">Not linked</ui-badge>
          }
          @if (contact.origin === 'Mirrored') {
            <ui-badge i18n="@@contacts.auto">added automatically</ui-badge>
          }
        </div>
      </div>
      <div dialog-actions class="contents">
        <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.close">Close</button>
        @if (contact.pendingLinkRequestId) {
          <button uiButton type="button" (click)="ref.close('cancelLink')" i18n="@@contacts.cancelLink">Cancel request</button>
        } @else if (!contact.isLinked) {
          <button uiButton type="button" (click)="ref.close('link')">
            <ui-icon name="link" size="sm" /><ng-container i18n="@@contacts.link">Link</ng-container>
          </button>
        }
      </div>
    </ui-dialog>
  `,
})
export class ContactDetailDialog {
  protected readonly contact = inject<Contact>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<ContactAction>>(DialogRef);
}
