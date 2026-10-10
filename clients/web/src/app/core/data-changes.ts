import { Injectable, signal } from '@angular/core';

/** Other modules update their views (e.g. wallet balances) a moment after a change. */
export const EVENTUAL_REFRESH_DELAY_MS = 1500;

/**
 * Bumped whenever something changed the user's money (a form was saved or an asynchronous
 * operation completed). Pages include it in their resources so they reload.
 */
@Injectable({ providedIn: 'root' })
export class DataChanges {
  readonly version = signal(0);
  private followUp: ReturnType<typeof setTimeout> | null = null;

  /** Reloads now, and once more shortly after to pick up asynchronous updates. */
  notify(): void {
    this.bump();

    if (this.followUp) clearTimeout(this.followUp);
    this.followUp = setTimeout(() => {
      this.followUp = null;
      this.bump();
    }, EVENTUAL_REFRESH_DELAY_MS);
  }

  private bump(): void {
    this.version.update((v) => v + 1);
  }
}
