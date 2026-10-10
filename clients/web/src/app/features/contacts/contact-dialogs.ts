import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { Contact } from '../../core/api/models';
import { Button, DialogLayout, FormField, Input } from '../../ui';

@Component({
  selector: 'app-contact-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog i18n-heading="@@contacts.new" heading="New contact">
        <div class="grid gap-4">
          <ui-form-field i18n-label="@@contacts.name" label="Name">
            <input uiInput formControlName="name" maxlength="200" />
          </ui-form-field>
          <ui-form-field i18n-label="@@contacts.email" label="Email"
                         i18n-hint="@@contacts.emailHint" hint="Optional. If it belongs to a Ledger user, shared debts also appear in their account.">
            <input uiInput type="email" formControlName="email" />
          </ui-form-field>
        </div>
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton type="submit" [disabled]="form.invalid" [loading]="saving()" i18n="@@ui.create">Create</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class ContactDialog {
  private readonly api = inject(LedgerApi);
  protected readonly ref = inject<DialogRef<Contact>>(DialogRef);
  protected readonly saving = signal(false);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    email: ['', Validators.email],
  });

  protected async save(): Promise<void> {
    if (this.form.invalid) return;

    const { name, email } = this.form.getRawValue();
    this.saving.set(true);

    try {
      this.ref.close(await firstValueFrom(this.api.createContact(name.trim(), email.trim() || null)));
    } catch {
      this.saving.set(false);
    }
  }
}

@Component({
  selector: 'app-link-contact-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog i18n-heading="@@contacts.linkTitle" heading="Link to a Ledger user" [description]="description">
        <ui-form-field i18n-label="@@contacts.userEmail" label="Email of the user">
          <input uiInput type="email" formControlName="email" />
        </ui-form-field>
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton type="submit" [disabled]="form.invalid" [loading]="saving()">{{ submitLabel }}</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class LinkContactDialog {
  private readonly api = inject(LedgerApi);
  private readonly contact = inject<Contact>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<Contact>>(DialogRef);
  protected readonly saving = signal(false);

  /** A contact already used in debts or groups needs the user's approval: their books get the shared history. */
  protected readonly description = this.contact.isInUse
    ? $localize`:@@contacts.linkRequestDescription:${this.contact.name}:name: already has debts or groups with you. They will be asked to accept: once they do, that history is copied into their account and you share everything from then on.`
    : $localize`:@@contacts.linkDescription:Debts you share with this contact will also appear in their account.`;
  protected readonly submitLabel = this.contact.isInUse
    ? $localize`:@@contacts.sendLinkRequest:Send request`
    : $localize`:@@contacts.link:Link`;

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: [this.contact.email ?? '', [Validators.required, Validators.email]],
  });

  protected async save(): Promise<void> {
    if (this.form.invalid) return;

    this.saving.set(true);

    try {
      this.ref.close(await firstValueFrom(this.api.linkContact(this.contact.id, this.form.getRawValue().email.trim())));
    } catch {
      this.saving.set(false);
    }
  }
}
