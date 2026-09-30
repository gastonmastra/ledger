import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Button, DialogLayout, FormField, Input } from '../../ui';

export interface NameDialogData {
  title: string;
  label: string;
  value: string;
  maxLength: number;
}

/** Asks for a single name (rename a wallet, a category…). Closes with the new name. */
@Component({
  selector: 'app-name-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog [heading]="data.title">
        <ui-form-field [label]="data.label">
          <input uiInput formControlName="name" [maxlength]="data.maxLength" />
        </ui-form-field>
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton type="submit" [disabled]="form.invalid" i18n="@@ui.save">Save</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class NameDialog {
  protected readonly data = inject<NameDialogData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<string>>(DialogRef);
  protected readonly form = inject(NonNullableFormBuilder).group({
    name: [this.data.value, [Validators.required, Validators.maxLength(this.data.maxLength)]],
  });

  protected save(): void {
    if (this.form.valid) this.ref.close(this.form.getRawValue().name.trim());
  }
}
