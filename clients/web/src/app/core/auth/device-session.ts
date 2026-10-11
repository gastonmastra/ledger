import { isStandalone } from '../pwa/display-mode';

const PREFERENCE_KEY = 'ledger.rememberDevice';

/** Keys angular-oauth2-oidc stores (tokens, their expiry, and the login round trip). */
export const OAUTH_KEYS = [
  'access_token',
  'access_token_stored_at',
  'expires_at',
  'granted_scopes',
  'id_token',
  'id_token_claims_obj',
  'id_token_expires_at',
  'id_token_stored_at',
  'nonce',
  'PKCE_verifier',
  'refresh_token',
  'session_state',
] as const;

/**
 * "Remember me on this device": the login is kept in localStorage (it survives closing the app) and Keycloak is
 * asked for an offline token (a long-lived refresh token). Otherwise it is kept in sessionStorage as before.
 * Default: on when the app is installed, off in a browser tab.
 */
export class DeviceSession {
  constructor(
    private readonly local: Storage = localStorage,
    private readonly session: Storage = sessionStorage,
    private readonly standalone: () => boolean = () => isStandalone(),
  ) {}

  get remember(): boolean {
    const stored = safe(() => this.local.getItem(PREFERENCE_KEY));
    return stored === null || stored === undefined ? this.standalone() : stored === 'true';
  }

  /** Where the tokens live now. */
  storage(): Storage {
    return this.remember ? this.local : this.session;
  }

  /** Changes the preference and moves the current login to the new place. */
  setRemember(remember: boolean): void {
    const from = this.storage();
    safe(() => this.local.setItem(PREFERENCE_KEY, String(remember)));
    const to = this.storage();

    if (from === to) return;

    for (const key of OAUTH_KEYS) {
      const value = safe(() => from.getItem(key));
      if (value !== null && value !== undefined) safe(() => to.setItem(key, value));
      safe(() => from.removeItem(key));
    }
  }

  /** Removes the login from both places (logging out). */
  clear(): void {
    for (const key of OAUTH_KEYS) {
      safe(() => this.local.removeItem(key));
      safe(() => this.session.removeItem(key));
    }
  }
}

/** Storage can be unavailable (private mode, blocked site data). */
function safe<T>(read: () => T): T | undefined {
  try {
    return read();
  } catch {
    return undefined;
  }
}

/** The one used by the app (tests create their own with fake storages). */
export const deviceSession = new DeviceSession();
