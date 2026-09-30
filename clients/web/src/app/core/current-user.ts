import { Injectable, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { LedgerApi } from './api/ledger-api';

/** The logged-in user as known by the API; its personId identifies "me" in splits. */
@Injectable({ providedIn: 'root' })
export class CurrentUser {
  private readonly api = inject(LedgerApi);

  readonly me = rxResource({ stream: () => this.api.me() });
}
