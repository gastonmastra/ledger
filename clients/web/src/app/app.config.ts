import { provideHttpClient, withInterceptors, withInterceptorsFromDi } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideOAuthClient } from 'angular-oauth2-oidc';
import { routes } from './app.routes';
import { apiErrorInterceptor } from './core/api/http-interceptors';
import { AuthService } from './core/auth/auth.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    // The OAuth interceptor (DI based) adds the access token to /api requests.
    provideHttpClient(withInterceptors([apiErrorInterceptor]), withInterceptorsFromDi()),
    provideOAuthClient({ resourceServer: { allowedUrls: ['/api'], sendAccessToken: true } }),
    // Nothing renders until the user is logged in with Keycloak.
    provideAppInitializer(() => inject(AuthService).init()),
  ],
};
