import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { Asset } from '../../core/api/models';
import { Button, DateTimeInput, DialogLayout, FormField, Input, nowIso } from '../../ui';
import { PricePipe } from './formatting';

/** Records the current buy and/or sell price of an asset. No money moves: it only values the holding. */
@Component({
  selector: 'app-price-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button, DateTimeInput, PricePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog i18n-heading="@@price.title" heading="Update price">
        <div class="grid gap-4">
          <p class="text-sm text-muted" i18n="@@price.explanation">
            Your holding of {{ asset.name }} is valued at its sell price (what you would get if you sold it). No money moves.
          </p>
          <div class="grid gap-4 sm:grid-cols-2">
            <ui-form-field i18n-label="@@priceSide.sell" label="Sell price"
                           [hint]="asset.sellPrice ? currentLabel + ' ' + (asset.sellPrice | price: asset.currency) : null">
              <input uiInput type="number" min="0" step="any" formControlName="sellPrice" />
            </ui-form-field>
            <ui-form-field i18n-label="@@priceSide.buy" label="Buy price"
                           [hint]="asset.buyPrice ? currentLabel + ' ' + (asset.buyPrice | price: asset.currency) : null">
              <input uiInput type="number" min="0" step="any" formControlName="buyPrice" />
            </ui-form-field>
          </div>
          <ui-form-field i18n-label="@@ui.when" label="When">
            <ui-datetime-input formControlName="occurredAt" />
          </ui-form-field>
        </div>
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton type="submit" [disabled]="!canSave()" [loading]="saving()" i18n="@@ui.save">Save</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class PriceDialog {
  private readonly api = inject(LedgerApi);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly asset = inject<Asset>(DIALOG_DATA);
  protected readonly saving = signal(false);
  protected readonly currentLabel = $localize`:@@price.current:Current:`;

  protected readonly form = inject(NonNullableFormBuilder).group({
    sellPrice: [null as number | null],
    buyPrice: [null as number | null],
    occurredAt: [nowIso(), Validators.required],
  });

  private readonly value = toSignal(this.form.valueChanges, { initialValue: this.form.value });

  protected readonly canSave = computed(() => (this.value().sellPrice ?? 0) > 0 || (this.value().buyPrice ?? 0) > 0);

  protected async save(): Promise<void> {
    if (!this.canSave()) return;

    const value = this.form.getRawValue();
    this.saving.set(true);

    try {
      await firstValueFrom(
        this.api.recordPrice(this.asset.id, {
          sellPrice: value.sellPrice && value.sellPrice > 0 ? value.sellPrice : null,
          buyPrice: value.buyPrice && value.buyPrice > 0 ? value.buyPrice : null,
          occurredAt: value.occurredAt,
        }),
      );
      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}
