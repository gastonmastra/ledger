import { LOCALE_ID, Pipe, PipeTransform, inject } from '@angular/core';

/** Unit price of an asset: a currency amount that may need up to 8 decimals (e.g. 0.00001234 $). */
@Pipe({ name: 'price' })
export class PricePipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);
  private readonly formatters = new Map<string, Intl.NumberFormat>();

  transform(price: number | null | undefined, currency: string): string {
    if (price === null || price === undefined) return '';

    let formatter = this.formatters.get(currency);

    if (!formatter) {
      formatter = new Intl.NumberFormat(this.locale, {
        style: 'currency',
        currency,
        minimumFractionDigits: 2,
        maximumFractionDigits: 8,
      });
      this.formatters.set(currency, formatter);
    }

    return formatter.format(price);
  }
}

/** Quantity of an asset: up to 12 decimals, no trailing zeros. */
@Pipe({ name: 'quantity' })
export class QuantityPipe implements PipeTransform {
  private readonly format = new Intl.NumberFormat(inject(LOCALE_ID), { maximumFractionDigits: 12 });

  transform(quantity: number | null | undefined): string {
    return quantity === null || quantity === undefined ? '' : this.format.format(quantity);
  }
}

/** Quantity × unit price rounded to the cent, like the API does. */
export function tradeTotal(quantity: number | null | undefined, unitPrice: number | null | undefined): number {
  if (!quantity || !unitPrice) return 0;
  return Math.round(quantity * unitPrice * 100) / 100;
}
