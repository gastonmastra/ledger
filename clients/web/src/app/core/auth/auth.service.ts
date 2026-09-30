import { Injectable, computed, inject, signal } from '@angular/core';
import { AuthConfig, OAuthService } from 'angular-oauth2-oidc';
import { appSettings } from '../../app.settings';

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

  /** Runs before the app starts: returns only once the user is logged in (otherwise redirects to Keycloak). */
  async init(): Promise<void> {
    // document.baseURI includes the locale base href (/en/ or /es/), so we come back to the same language.
    const config: AuthConfig = {
      issuer: appSettings.keycloak.issuer,
      clientId: appSettings.keycloak.clientId,
      responseType: 'code',
      scope: 'openid profile email',
      redirectUri: document.baseURI,
      postLogoutRedirectUri: document.baseURI,
      requireHttps: false,
      useSilentRefresh: false,
      sessionChecksEnabled: false,
      clearHashAfterLogin: true,
    };

    this.oauth.configure(config);
    await this.oauth.loadDiscoveryDocumentAndLogin();

    if (!this.oauth.hasValidAccessToken()) {
      // loadDiscoveryDocumentAndLogin started the redirect to Keycloak: keep the app from booting.
      await new Promise(() => undefined);
    }

    this.oauth.setupAutomaticSilentRefresh();
    this.claims.set(this.oauth.getIdentityClaims() as Record<string, unknown>);
  }

  logout(): void {
    this.oauth.logOut();
  }
}
