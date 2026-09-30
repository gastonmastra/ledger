import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, Injectable, inject } from '@angular/core';
import { Button } from './button';
import { DialogLayout, DialogService } from './dialog';

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
}
