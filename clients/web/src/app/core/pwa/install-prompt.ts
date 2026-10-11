import { Injectable, computed, signal } from '@angular/core';
import { isIos, isStandalone } from './display-mode';

/** The browser's install prompt (Chrome, Edge, Android), kept until the user asks to install. */
interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

export type InstallState = 'installed' | 'available' | 'ios' | 'unavailable';

/**
 * Installing the app: on Android and desktop Chrome/Edge the browser offers a prompt we can show from a button;
 * on iPhone/iPad it is "Share → Add to Home Screen". Created at startup, so the prompt is not missed.
 */
@Injectable({ providedIn: 'root' })
export class InstallPrompt {
  private readonly deferred = signal<BeforeInstallPromptEvent | null>(null);
  private readonly installed = signal(isStandalone());

  readonly state = computed<InstallState>(() => {
    if (this.installed()) return 'installed';
    if (this.deferred()) return 'available';
    return isIos() ? 'ios' : 'unavailable';
  });

  constructor() {
    window.addEventListener('beforeinstallprompt', (event) => {
      event.preventDefault();
      this.deferred.set(event as BeforeInstallPromptEvent);
    });
    window.addEventListener('appinstalled', () => {
      this.deferred.set(null);
      this.installed.set(true);
    });
  }

  /** Shows the browser's install dialog; true when the user accepted. */
  async install(): Promise<boolean> {
    const event = this.deferred();
    if (!event) return false;

    await event.prompt();
    const { outcome } = await event.userChoice;
    this.deferred.set(null);
    return outcome === 'accepted';
  }
}
