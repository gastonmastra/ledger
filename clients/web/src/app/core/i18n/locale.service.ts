import { DOCUMENT, Injectable, LOCALE_ID, inject } from '@angular/core';
import { Router } from '@angular/router';

export interface AppLocale {
  code: 'en' | 'es';
  label: string;
}

/** Languages built by Angular i18n. Each one is served under its own base href (/en/, /es/). */
export const LOCALES: readonly AppLocale[] = [
  { code: 'en', label: 'English' },
  { code: 'es', label: 'Español' },
];

export const LOCALE_COOKIE = 'ledger_locale';

@Injectable({ providedIn: 'root' })
export class LocaleService {
  private readonly document = inject(DOCUMENT);
  private readonly router = inject(Router);

  /** Language of the running build ("en" or "es"). */
  readonly current = inject(LOCALE_ID).slice(0, 2) as AppLocale['code'];

  /**
   * Opens the same page in another language. The choice is remembered in a cookie, which nginx
   * uses to redirect "/" to the right language.
   * Note: `ng serve` serves a single language; see the web app README.
   */
  switchTo(code: AppLocale['code']): void {
    if (code === this.current) return;

    this.document.cookie = `${LOCALE_COOKIE}=${code}; path=/; max-age=31536000; SameSite=Lax`;
    this.document.location.href = `/${code}${this.router.url}`;
  }
}
