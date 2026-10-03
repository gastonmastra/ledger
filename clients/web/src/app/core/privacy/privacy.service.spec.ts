import { TestBed } from '@angular/core/testing';
import { MASK, PrivacyService, maskNumber } from './privacy.service';

describe('PrivacyService', () => {
  beforeEach(() => localStorage.clear());

  it('masks the digits and the sign but keeps the currency', () => {
    const en = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'EUR', signDisplay: 'exceptZero' });
    const es = new Intl.NumberFormat('es', { style: 'currency', currency: 'EUR' });

    expect(maskNumber(en, -1234.5)).toBe(`€${MASK}`);
    expect(maskNumber(en, 0.5)).toBe(`€${MASK}`);
    expect(maskNumber(es, 1234.5)).toBe(`${MASK}\u00a0€`); // Intl uses a non-breaking space
  });

  it('toggles with Shift+H, except while typing, and remembers the choice', () => {
    const privacy = TestBed.inject(PrivacyService);
    expect(privacy.hidden()).toBe(false);

    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'KeyH', shiftKey: true }));
    expect(privacy.hidden()).toBe(true);

    const input = document.createElement('input');
    document.body.appendChild(input);
    input.dispatchEvent(new KeyboardEvent('keydown', { code: 'KeyH', shiftKey: true, bubbles: true }));
    expect(privacy.hidden()).toBe(true);
    input.remove();

    TestBed.tick();
    expect(localStorage.getItem('ledger.hideAmounts')).toBe('true');
  });
});
