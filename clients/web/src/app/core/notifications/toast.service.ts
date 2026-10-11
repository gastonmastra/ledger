import { Injectable, signal } from '@angular/core';

export type ToastTone = 'info' | 'success' | 'error';

/** A button in the toast (e.g. "Reload"). Running it also dismisses the toast. */
export interface ToastAction {
  label: string;
  run: () => void;
}

export interface Toast {
  id: number;
  tone: ToastTone;
  message: string;
  action?: ToastAction;
}

/** Short messages shown in the corner of the screen (see ui/toast). */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;
  readonly toasts = signal<Toast[]>([]);

  info(message: string): number {
    return this.show('info', message);
  }

  success(message: string): number {
    return this.show('success', message);
  }

  error(message: string): number {
    return this.show('error', message, 8000);
  }

  /** A toast with a button that stays until it is used or dismissed (e.g. a new version is available). */
  withAction(message: string, action: ToastAction, tone: ToastTone = 'info'): number {
    return this.show(tone, message, 0, action);
  }

  dismiss(id: number): void {
    this.toasts.update((toasts) => toasts.filter((t) => t.id !== id));
  }

  /** durationMs 0: stays until dismissed. */
  private show(tone: ToastTone, message: string, durationMs = 4000, action?: ToastAction): number {
    const id = this.nextId++;
    this.toasts.update((toasts) => [...toasts, { id, tone, message, action }]);
    if (durationMs > 0) setTimeout(() => this.dismiss(id), durationMs);
    return id;
  }
}
