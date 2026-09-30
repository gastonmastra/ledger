import { LOCALE_ID, Pipe, PipeTransform, inject } from '@angular/core';

/**
 * Formats an amount with its currency in the app language:
 * en → "€1,234.50", es → "1234,50 €".
 */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);
  private readonly formatters = new Map<string, Intl.NumberFormat>();

  transform(amount: number | null | undefined, currency: string, signed = false): string {
    if (amount === null || amount === undefined) return '';

    const key = `${currency}|${signed}`;
    let formatter = this.formatters.get(key);

    if (!formatter) {
      formatter = new Intl.NumberFormat(this.locale, {
        style: 'currency',
        currency,
        signDisplay: signed ? 'exceptZero' : 'auto',
      });
      this.formatters.set(key, formatter);
    }

    return formatter.format(amount);
  }
}
