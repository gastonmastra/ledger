/**
 * Dates are chosen in the user's local time and sent to the API as ISO strings in UTC
 * (the API stores DateTimeOffset in UTC, so it does not matter where the user is).
 */

/** Value for an <input type="datetime-local"> (local time, minutes precision). */
export function toLocalInputValue(date: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');

  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}`
  );
}

/** Converts the value of an <input type="datetime-local"> to an ISO string in UTC. */
export function localInputToIso(value: string): string {
  return new Date(value).toISOString();
}

/** Converts an ISO date from the API to the value of an <input type="datetime-local">. */
export function isoToLocalInput(iso: string): string {
  return toLocalInputValue(new Date(iso));
}

export function isInFuture(value: string, now: Date = new Date()): boolean {
  return new Date(value).getTime() > now.getTime() + 60_000;
}

/** The browser's IANA time zone (e.g. "Europe/Madrid"), used by the reports. */
export function currentTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
}
