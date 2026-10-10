import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { ASSET_TYPES, Asset, AssetType, CURRENCIES, Currency } from '../../core/api/models';
import { assetTypeLabel } from '../../core/i18n/labels';
import { Button, DialogLayout, FormField, Input } from '../../ui';

/** Adds an asset to my catalogue, or (opened with an asset) changes its name and symbol. */
@Component({
  selector: 'app-asset-dialog',
  imports: [ReactiveFormsModule, DialogLayout, FormField, Input, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()">
      <ui-dialog [heading]="heading">
        <div class="grid gap-4">
          <div class="grid gap-4 sm:grid-cols-[2fr_1fr]">
            <ui-form-field i18n-label="@@asset.name" label="Name">
              <input uiInput formControlName="name" maxlength="100" i18n-placeholder="@@asset.namePlaceholder" placeholder="e.g. Bitcoin" />
            </ui-form-field>
            <ui-form-field i18n-label="@@asset.symbol" label="Symbol" i18n-hint="@@ui.optional" hint="Optional">
              <input uiInput formControlName="symbol" maxlength="20" i18n-placeholder="@@asset.symbolPlaceholder" placeholder="e.g. BTC" />
            </ui-form-field>
          </div>
          @if (!editing) {
            <div class="grid gap-4 sm:grid-cols-2">
              <ui-form-field i18n-label="@@asset.type" label="Type">
                <select uiInput formControlName="type">
                  @for (type of types; track type) {
                    <option [value]="type">{{ typeLabel(type) }}</option>
                  }
                </select>
              </ui-form-field>
              <ui-form-field i18n-label="@@asset.currency" label="Quoted in"
                             i18n-hint="@@asset.currencyHint" hint="The currency of its price. It can't be changed later.">
                <select uiInput formControlName="currency">
                  @for (currency of currencies; track currency) {
                    <option [value]="currency">{{ currency }}</option>
                  }
                </select>
              </ui-form-field>
            </div>
          }
        </div>
        <div dialog-actions class="contents">
          <button uiButton variant="secondary" type="button" (click)="ref.close()" i18n="@@ui.cancel">Cancel</button>
          <button uiButton type="submit" [disabled]="form.invalid" [loading]="saving()">{{ submitLabel }}</button>
        </div>
      </ui-dialog>
    </form>
  `,
})
export class AssetDialog {
  private readonly api = inject(LedgerApi);
  protected readonly ref = inject<DialogRef<Asset | boolean>>(DialogRef);
  protected readonly editing = inject<Asset | null>(DIALOG_DATA, { optional: true }) ?? null;
  protected readonly saving = signal(false);

  protected readonly types = ASSET_TYPES;
  protected readonly currencies = CURRENCIES;
  protected readonly typeLabel = assetTypeLabel;

  protected readonly heading = this.editing
    ? $localize`:@@asset.editTitle:Edit asset`
    : $localize`:@@asset.newTitle:New asset`;
  protected readonly submitLabel = this.editing ? $localize`:@@ui.save:Save` : $localize`:@@asset.create:Add asset`;

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: [this.editing?.name ?? '', [Validators.required, Validators.maxLength(100)]],
    symbol: [this.editing?.symbol ?? '', Validators.maxLength(20)],
    type: ['Stock' as AssetType],
    currency: ['USD' as Currency],
  });

  protected async save(): Promise<void> {
    if (this.form.invalid) return;

    const value = this.form.getRawValue();
    const symbol = value.symbol.trim() || null;
    this.saving.set(true);

    try {
      if (this.editing) {
        await firstValueFrom(this.api.updateAsset(this.editing.id, value.name.trim(), symbol));
        this.ref.close(true);
      } else {
        const asset = await firstValueFrom(
          this.api.createAsset({ name: value.name.trim(), symbol, type: value.type, currency: value.currency }),
        );
        this.ref.close(asset);
      }
    } catch {
      this.saving.set(false);
    }
  }
}
