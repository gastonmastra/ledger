import { LOCALE_ID, Pipe, PipeTransform, inject } from '@angular/core';
import { PrivacyService, maskNumber } from '../core/privacy/privacy.service';

/**
 * Formats an amount with its currency in the app language:
 * en → "€1,234.50", es → "1234,50 €". With "hide amounts" on: "€•••••".
 * Not pure: it must update when "hide amounts" is toggled (reading the signal re-renders the view).
 */
@Pipe({ name: 'money', pure: false })
export class MoneyPipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);
  private readonly privacy = inject(PrivacyService);
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

    return this.privacy.hidden() ? maskNumber(formatter, amount) : formatter.format(amount);
  }
}
