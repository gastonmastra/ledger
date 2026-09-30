import { Injectable, signal } from '@angular/core';

export type ToastTone = 'info' | 'success' | 'error';

export interface Toast {
  id: number;
  tone: ToastTone;
  message: string;
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

  dismiss(id: number): void {
    this.toasts.update((toasts) => toasts.filter((t) => t.id !== id));
  }

  private show(tone: ToastTone, message: string, durationMs = 4000): number {
    const id = this.nextId++;
    this.toasts.update((toasts) => [...toasts, { id, tone, message }]);
    setTimeout(() => this.dismiss(id), durationMs);
    return id;
  }
}
