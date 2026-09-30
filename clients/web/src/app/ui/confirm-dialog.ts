import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, Injectable, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from './button';
import { DialogLayout, DialogService } from './dialog';
import { ReasonField } from './reason-field';

export interface ConfirmOptions {
  title: string;
  message: string;
  confirmLabel: string;
  danger?: boolean;
}

@Component({
  selector: 'ui-confirm-dialog',
  imports: [DialogLayout, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-dialog [heading]="data.title">
      <p class="text-sm text-muted">{{ data.message }}</p>
      <div dialog-actions class="contents">
        <button uiButton variant="secondary" type="button" (click)="ref.close(false)" i18n="@@ui.cancel">Cancel</button>
        <button uiButton [variant]="data.danger ? 'danger' : 'primary'" type="button" (click)="ref.close(true)">
          {{ data.confirmLabel }}
        </button>
      </div>
    </ui-dialog>
  `,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmOptions>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
}

@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly dialogs = inject(DialogService);

  async confirm(options: ConfirmOptions): Promise<boolean> {
    return (await this.dialogs.open<boolean, ConfirmOptions>(ConfirmDialog, options)) === true;
  }

  /** Asks for confirmation and a reason. Resolves to the reason, or null if cancelled. */
  async askReason(options: ReasonOptions): Promise<string | null> {
    const reason = await this.dialogs.open<string, ReasonOptions>(ReasonDialog, options);
    return reason === undefined ? null : reason;
  }
}

export interface ReasonOptions extends ConfirmOptions {
  /** False: the reason is optional (e.g. rejecting a change). */
  required?: boolean;
  label?: string;
  hint?: string;
}

/** Confirmation that also asks why (deletions, restores, answers to changes). */
@Component({
  selector: 'ui-reason-dialog',
  imports: [DialogLayout, Button, ReasonField, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form (ngSubmit)="submit()">
      <ui-dialog [heading]="data.title">
        <div class="grid gap-4">
          <p class="text-sm text-muted">{{ data.message }}</p>
          <ui-reason-field name="reason" [ngModel]="reason()" (ngModelChange)="reason.set($event)" [required]="required"
                           [label]="data.label ?? null" [hint]="data.hint ?? null" />
        </div>
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton [variant]="data.danger ? 'danger' : 'primary'" type="submit" [disabled]="!valid()">
            {{ data.confirmLabel }}
          </button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class ReasonDialog {
  protected readonly data = inject<ReasonOptions>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<string>>(DialogRef);
  protected readonly required = this.data.required ?? true;
  protected readonly reason = signal('');
  protected readonly valid = computed(() => !this.required || this.reason().trim().length > 0);

  protected submit(): void {
    if (this.valid()) this.ref.close(this.reason().trim());
  }
}
