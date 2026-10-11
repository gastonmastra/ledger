import { provideHttpClient, withInterceptors, withInterceptorsFromDi } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideServiceWorker } from '@angular/service-worker';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { OAuthStorage, provideOAuthClient } from 'angular-oauth2-oidc';
import { routes } from './app.routes';
import { apiErrorInterceptor } from './core/api/http-interceptors';
import { AuthService } from './core/auth/auth.service';
import { deviceSession } from './core/auth/device-session';
import { AppUpdates } from './core/pwa/app-updates';
import { InstallPrompt } from './core/pwa/install-prompt';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    // The OAuth interceptor (DI based) adds the access token to /api requests.
    provideHttpClient(withInterceptors([apiErrorInterceptor]), withInterceptorsFromDi()),
    provideOAuthClient({ resourceServer: { allowedUrls: ['/api'], sendAccessToken: true } }),
    // Tokens in localStorage when the login is remembered on this device, else in sessionStorage.
    { provide: OAuthStorage, useFactory: () => deviceSession.storage() },
    // Installable app: caches the app (never the API) and updates it in the background. Not in `ng serve`.
    // sw.js = Angular's worker plus a rule that leaves Keycloak requests to the browser (public/sw.js).
    provideServiceWorker('sw.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
    // Before the login redirect too: the browser's install prompt can come at any time.
    provideAppInitializer(() => {
      inject(InstallPrompt);
      inject(AppUpdates).start();
    }),
    // Nothing renders until the user is logged in with Keycloak.
    provideAppInitializer(() => inject(AuthService).init()),
  ],
};
