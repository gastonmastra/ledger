/**
 * Runtime settings. The browser talks to Keycloak directly and to the API through the same
 * origin (/api is proxied by the dev server or by nginx), so no CORS is needed.
 */
export const appSettings = {
  keycloak: {
    issuer: 'http://localhost:8080/realms/ledger',
    clientId: 'ledger-web',
  },
  /** How long to keep checking an asynchronous operation (loan, split, settlement). */
  operationTimeoutMs: 60_000,
  operationPollMs: 1_000,
} as const;
