import { Injectable, signal } from '@angular/core';

/** Whether the device is online (the shell shows a notice when it is not). */
@Injectable({ providedIn: 'root' })
export class Network {
  readonly online = signal(navigator.onLine);

  constructor() {
    window.addEventListener('online', () => this.online.set(true));
    window.addEventListener('offline', () => this.online.set(false));
  }
}
