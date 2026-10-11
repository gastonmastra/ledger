import { Injectable, inject } from '@angular/core';
import { SwUpdate } from '@angular/service-worker';
import { ToastService } from '../notifications/toast.service';

/** How often an open app checks for a new version (also when it comes back to the foreground). */
const CHECK_EVERY_MS = 30 * 60 * 1000;

/**
 * After a deploy the service worker downloads the new version in the background: this offers to reload into it,
 * instead of running the old one until the app is restarted. Started once at startup.
 */
@Injectable({ providedIn: 'root' })
export class AppUpdates {
  private readonly updates = inject(SwUpdate);
  private readonly toasts = inject(ToastService);
  private offered = false;

  start(): void {
    if (!this.updates.isEnabled) return;

    this.updates.versionUpdates.subscribe((event) => {
      if (event.type === 'VERSION_READY' && !this.offered) {
        this.offered = true;
        this.toasts.withAction($localize`:@@pwa.updateReady:A new version of Ledger is available.`, {
          label: $localize`:@@pwa.reload:Reload`,
          run: () => document.location.reload(),
        });
      }
    });

    // The cached version can no longer be served (e.g. its files were removed): load the current one.
    this.updates.unrecoverable.subscribe(() => document.location.reload());

    const check = () => void this.updates.checkForUpdate().catch(() => undefined);
    setInterval(check, CHECK_EVERY_MS);
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') check();
    });
  }
}
