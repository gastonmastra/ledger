import { DOCUMENT, Injectable, computed, effect, inject, signal } from '@angular/core';

export type ThemeMode = 'system' | 'light' | 'dark';

const STORAGE_KEY = 'ledger.theme';

/** Light/dark theme: follows the OS by default; the user can force one. */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly media = this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)');
  private readonly systemDark = signal(this.media?.matches ?? false);

  readonly mode = signal<ThemeMode>(readStoredMode());
  readonly isDark = computed(() => this.mode() === 'dark' || (this.mode() === 'system' && this.systemDark()));

  constructor() {
    this.media?.addEventListener('change', (event) => this.systemDark.set(event.matches));

    effect(() => {
      this.document.documentElement.classList.toggle('dark', this.isDark());
      try {
        localStorage.setItem(STORAGE_KEY, this.mode());
      } catch {
        // Storage can be unavailable (private mode); the theme still works for this visit.
      }
    });
  }

  /** system → light → dark → system */
  cycle(): void {
    const next: Record<ThemeMode, ThemeMode> = { system: 'light', light: 'dark', dark: 'system' };
    this.mode.set(next[this.mode()]);
  }
}

function readStoredMode(): ThemeMode {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored === 'light' || stored === 'dark' ? stored : 'system';
  } catch {
    return 'system';
  }
}
