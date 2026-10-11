// Service worker of the installable app: Angular's worker (ngsw-worker.js) plus one rule.
// Requests to other origins (Keycloak: login, tokens, logout) are left to the browser: Angular's worker would
// re-send them itself, and a cross-origin token request made from the worker fails ("Failed to fetch").
self.addEventListener('fetch', (event) => {
  if (new URL(event.request.url).origin !== self.location.origin) {
    event.stopImmediatePropagation(); // no respondWith: the browser does the request as usual
  }
});

importScripts('./ngsw-worker.js');
