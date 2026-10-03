import { DOCUMENT, Injectable, effect, inject, signal } from '@angular/core';

const STORAGE_KEY = 'ledger.hideAmounts';

/** What a hidden number shows instead of its digits. */
export const MASK = '•••••';

/**
 * "Hide amounts" mode: money, asset quantities, prices and chart values are masked, e.g. while
 * sharing the screen. Remembered in this browser; toggled from the top bar or with Shift+H.
 */
@Injectable({ providedIn: 'root' })
export class PrivacyService {
  private readonly document = inject(DOCUMENT);

  readonly hidden = signal(readStored());

  constructor() {
    effect(() => {
      try {
        localStorage.setItem(STORAGE_KEY, String(this.hidden()));
      } catch {
        // Storage can be unavailable (private mode); the choice still works for this visit.
      }
    });

    this.document.addEventListener('keydown', (event) => {
      if (isShortcut(event) && !isTyping(event.target)) {
        event.preventDefault();
        this.toggle();
      }
    });
  }

  toggle(): void {
    this.hidden.update((hidden) => !hidden);
  }
}

/**
 * Formats a number with its currency symbol (or other surrounding text) but replaces the digits
 * and the sign with {@link MASK}: "€•••••" in English, "••••• €" in Spanish.
 */
export function maskNumber(format: Intl.NumberFormat, value: number): string {
  let masked = false;

  return format
    .formatToParts(value)
    .map((part) => {
      if (!['integer', 'group', 'decimal', 'fraction', 'minusSign', 'plusSign'].includes(part.type)) return part.value;
      if (masked) return '';
      masked = part.type === 'integer' || part.type === 'fraction' || part.type === 'decimal';
      return masked ? MASK : '';
    })
    .join('');
}

function isShortcut(event: KeyboardEvent): boolean {
  return event.shiftKey && !event.ctrlKey && !event.metaKey && !event.altKey && event.code === 'KeyH';
}

/** Shift+H inside a text field types an "H": don't toggle then. */
function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  return target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName);
}

function readStored(): boolean {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'true';
  } catch {
    return false;
  }
}
