import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { Group } from '../../core/api/models';
import { Button, DialogLayout, EmptyState, FormField, Input } from '../../ui';

/** Creates a group with me and some of my contacts. Closes with the new group. */
@Component({
  selector: 'app-group-dialog',
  imports: [FormsModule, DialogLayout, FormField, Input, Button, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog i18n-heading="@@groups.new" heading="New group"
               i18n-description="@@groups.newDescription" description="Members who are Ledger users will see the group too.">
      <div class="grid gap-4">
        <ui-form-field i18n-label="@@groups.name" label="Name">
          <input uiInput [ngModel]="name()" (ngModelChange)="name.set($event)" maxlength="100"
                 i18n-placeholder="@@groups.namePlaceholder" placeholder="e.g. Summer trip" />
        </ui-form-field>
        <fieldset>
          <legend class="mb-2 text-sm font-medium" i18n="@@groups.members">Members</legend>
          @if (!contacts.value()?.length) {
            <ui-empty-state i18n-message="@@groups.noContacts" message="Add contacts first to invite them." />
          }
          <ul class="grid gap-2">
            @for (contact of contacts.value(); track contact.id) {
              <li>
                <label class="flex cursor-pointer items-center gap-2 text-sm">
                  <input type="checkbox" class="accent-primary" [checked]="selected().has(contact.id)" (change)="toggle(contact.id)" />
                  {{ contact.name }}
                </label>
              </li>
            }
          </ul>
        </fieldset>
      </div>
      <div dialog-actions class="contents">
        <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
        <button uiButton type="button" [disabled]="!name().trim()" [loading]="saving()" (click)="save()" i18n="@@ui.create">Create</button>
      </div>
    </ui-dialog>
  `,
})
export class GroupDialog {
  private readonly api = inject(LedgerApi);
  protected readonly ref = inject<DialogRef<Group>>(DialogRef);
  protected readonly contacts = rxResource({ stream: () => this.api.contacts() });
  protected readonly name = signal('');
  protected readonly selected = signal(new Set<string>());
  protected readonly saving = signal(false);

  protected toggle(id: string): void {
    this.selected.update((set) => {
      const next = new Set(set);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  }

  protected async save(): Promise<void> {
    this.saving.set(true);

    try {
      this.ref.close(await firstValueFrom(this.api.createGroup(this.name().trim(), [...this.selected()])));
    } catch {
      this.saving.set(false);
    }
  }
}

/** Adds one of my contacts to a group. */
@Component({
  selector: 'app-add-member-dialog',
  imports: [FormsModule, DialogLayout, FormField, Input, Button, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog i18n-heading="@@groups.addMember" heading="Add member">
      @if (candidates().length === 0 && !contacts.isLoading()) {
        <ui-empty-state i18n-message="@@groups.noCandidates" message="All your contacts are already members." />
      } @else {
        <ui-form-field i18n-label="@@groups.contact" label="Contact">
          <select uiInput [ngModel]="contactId()" (ngModelChange)="contactId.set($event)">
            <option value="" disabled i18n="@@ui.choose">Choose…</option>
            @for (contact of candidates(); track contact.id) {
              <option [value]="contact.id">{{ contact.name }}</option>
            }
          </select>
        </ui-form-field>
      }
      <div dialog-actions class="contents">
        <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
        <button uiButton type="button" [disabled]="!contactId()" [loading]="saving()" (click)="save()" i18n="@@groups.add">Add</button>
      </div>
    </ui-dialog>
  `,
})
export class AddMemberDialog {
  private readonly api = inject(LedgerApi);
  private readonly group = inject<Group>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly contacts = rxResource({ stream: () => this.api.contacts() });
  protected readonly contactId = signal('');
  protected readonly saving = signal(false);

  protected readonly candidates = computed(() =>
    (this.contacts.value() ?? []).filter((c) => !this.group.members.some((m) => m.personId === c.personId)),
  );

  protected async save(): Promise<void> {
    this.saving.set(true);

    try {
      await firstValueFrom(this.api.addGroupMember(this.group.id, this.contactId()));
      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}
