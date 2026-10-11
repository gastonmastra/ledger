import { Injectable, computed, inject, signal } from '@angular/core';
import { AuthConfig, OAuthService } from 'angular-oauth2-oidc';
import { RuntimeConfig, appSettings } from '../../app.settings';
import { deviceSession } from './device-session';

/** Keycloak action that registers a passkey (Face ID, fingerprint, security key) for the user. */
const REGISTER_PASSKEY = 'webauthn-register-passwordless';

/** Keycloak login with the authorization code flow + PKCE, and automatic token refresh. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly oauth = inject(OAuthService);
  private readonly claims = signal<Record<string, unknown> | null>(null);

  readonly userName = computed(() => {
    const claims = this.claims();
    return (claims?.['name'] ?? claims?.['preferred_username'] ?? '') as string;
  });

  readonly email = computed(() => (this.claims()?.['email'] ?? '') as string);

  /** Remember me on this device (see DeviceSession). */
  readonly remember = signal(deviceSession.remember);

  /** Keycloak refused the long-lived token for "Remember me" (the account lacks offline access): it was turned off. */
  readonly rememberRefused = signal(consumeFlag(REMEMBER_REFUSED_KEY));

  /** Outcome of the last Keycloak action asked from the app (e.g. setting up a passkey), shown once. */
  readonly actionResult = signal<'success' | 'cancelled' | 'error' | null>(null);

  /** Runs before the app starts: returns only once the user is logged in (otherwise redirects to Keycloak). */
  async init(): Promise<void> {
    const { keycloakIssuer } = await loadRuntimeConfig();
    this.readActionResult();

    // document.baseURI includes the locale base href (/en/ or /es/), so we come back to the same language.
    const config: AuthConfig = {
      issuer: keycloakIssuer,
      clientId: appSettings.keycloak.clientId,
      responseType: 'code',
      // offline_access: a long-lived refresh token, only when the login is remembered on this device.
      scope: deviceSession.remember ? 'openid profile email offline_access' : 'openid profile email',
      redirectUri: document.baseURI,
      postLogoutRedirectUri: document.baseURI,
      // Plain HTTP only for a local Keycloak; a deployed one must be HTTPS.
      requireHttps: keycloakIssuer.startsWith('https:'),
      useSilentRefresh: false,
      sessionChecksEnabled: false,
      clearHashAfterLogin: true,
    };

    this.oauth.configure(config);

    try {
      await this.oauth.loadDiscoveryDocumentAndLogin();
    } catch (error) {
      // Keycloak refused the offline token "Remember me" asks for: turn it off and log in normally.
      if (!isOffline(error) && deviceSession.remember && config.scope?.includes('offline_access')) {
        deviceSession.setRemember(false);
        setFlag(REMEMBER_REFUSED_KEY);
        window.location.replace(document.baseURI);
        await new Promise(() => undefined);
      }

      // Offline (the installed app opened without a connection): start with what is stored, so the cached app
      // and the offline notice show. Back online, reload to log in normally.
      if (!isOffline(error)) throw error;

      this.claims.set(this.oauth.getIdentityClaims() as Record<string, unknown> | null);
      window.addEventListener('online', () => window.location.reload(), { once: true });
      return;
    }

    if (!this.oauth.hasValidAccessToken()) {
      // loadDiscoveryDocumentAndLogin started the redirect to Keycloak: keep the app from booting.
      await new Promise(() => undefined);
    }

    this.oauth.setupAutomaticSilentRefresh();
    this.claims.set(this.oauth.getIdentityClaims() as Record<string, unknown>);
    this.returnToRequestedPage();
  }

  logout(): void {
    const remembered = deviceSession.remember;
    const done = () => deviceSession.clear();

    // A remembered login holds an offline token: revoke it so it can't be used after logging out.
    if (remembered && this.oauth.getRefreshToken()) {
      void this.oauth.revokeTokenAndLogout().finally(done);
    } else {
      this.oauth.logOut();
      done();
    }
  }

  /**
   * Turns "Remember me on this device" on or off. Turning it on logs in again (usually without asking, Keycloak
   * still has the session) to get the long-lived token; the app comes back to Settings.
   */
  setRemember(remember: boolean): void {
    deviceSession.setRemember(remember);
    // The library keeps the storage it started with: point it to the new one (the login round trip below
    // writes its nonce there).
    this.oauth.setStorage(deviceSession.storage());
    this.remember.set(remember);

    if (remember) {
      this.oauth.scope = 'openid profile email offline_access';
      this.oauth.initCodeFlow('/settings');
    }
  }

  /** Sets up a passkey (Face ID, fingerprint…) in Keycloak, then comes back to Settings. */
  registerPasskey(): void {
    this.oauth.initCodeFlow('/settings', { kc_action: REGISTER_PASSKEY });
  }

  /** Keycloak's account page, where passkeys and passwords are managed. */
  accountUrl(): string {
    return `${this.oauth.issuer}/account/#/account-security/signing-in`;
  }

  /** After a login started from a page (e.g. Settings), go back to it instead of the home page. */
  private returnToRequestedPage(): void {
    const target = this.oauth.state ? decodeURIComponent(this.oauth.state) : '';
    if (!target.startsWith('/')) return;

    const base = document.baseURI.replace(/\/$/, '');
    window.history.replaceState(window.history.state, '', base + target);
  }

  /** Keycloak reports the result of an action it was asked for (kc_action) in the redirect. */
  private readActionResult(): void {
    const status = new URLSearchParams(window.location.search).get('kc_action_status');
    if (status === 'success' || status === 'cancelled' || status === 'error') this.actionResult.set(status);
  }
}

const RUNTIME_CONFIG_KEY = 'ledger.runtimeConfig';
const REMEMBER_REFUSED_KEY = 'ledger.rememberRefused';

function setFlag(key: string): void {
  try {
    sessionStorage.setItem(key, '1');
  } catch {
    // Storage unavailable: the message is just not shown.
  }
}

/** Reads a one-time flag and removes it. */
function consumeFlag(key: string): boolean {
  try {
    const set = sessionStorage.getItem(key) === '1';
    sessionStorage.removeItem(key);
    return set;
  } catch {
    return false;
  }
}

/** /config.json; offline, the copy kept from the last time it was read. */
async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  try {
    const response = await fetch(appSettings.runtimeConfigUrl, { cache: 'no-store' });
    if (!response.ok)
      throw new Error(`Could not load ${appSettings.runtimeConfigUrl} (HTTP ${response.status}).`);

    const config = (await response.json()) as RuntimeConfig;
    try {
      localStorage.setItem(RUNTIME_CONFIG_KEY, JSON.stringify(config));
    } catch {
      // Storage unavailable: it only matters offline.
    }
    return config;
  } catch (error) {
    const saved = (() => {
      try {
        return localStorage.getItem(RUNTIME_CONFIG_KEY);
      } catch {
        return null;
      }
    })();
    if (saved && isOffline(error)) return JSON.parse(saved) as RuntimeConfig;
    throw error;
  }
}

/** A failure because there is no connection (not, e.g., a server error). */
function isOffline(error: unknown): boolean {
  if (!navigator.onLine) return true;
  const status = (error as { status?: number } | null)?.status;
  return error instanceof TypeError || status === 0;
}
