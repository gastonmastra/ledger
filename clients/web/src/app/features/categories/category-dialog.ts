import { DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { CategoryType } from '../../core/api/models';
import { Button, DialogLayout, FormField, Input } from '../../ui';

@Component({
  selector: 'app-category-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog i18n-heading="@@categories.new" heading="New category">
        <div class="grid gap-4">
          <ui-form-field i18n-label="@@categories.name" label="Name">
            <input uiInput formControlName="name" maxlength="200" i18n-placeholder="@@categories.namePlaceholder" placeholder="e.g. Food" />
          </ui-form-field>
          <ui-form-field i18n-label="@@categories.type" label="Type">
            <select uiInput formControlName="type">
              <option value="Expense" i18n="@@categoryType.expense">Expense</option>
              <option value="Income" i18n="@@categoryType.income">Income</option>
            </select>
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
export class CategoryDialog {
  private readonly api = inject(LedgerApi);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly saving = signal(false);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    type: ['Expense' as CategoryType, Validators.required],
  });

  protected async save(): Promise<void> {
    if (this.form.invalid) return;

    const { name, type } = this.form.getRawValue();
    this.saving.set(true);

    try {
      await firstValueFrom(this.api.createCategory(name.trim(), type));
      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}
