import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MoneyPipe } from './money.pipe';

function pipeFor(locale: string): MoneyPipe {
  TestBed.configureTestingModule({ providers: [MoneyPipe, { provide: LOCALE_ID, useValue: locale }] });
  return TestBed.inject(MoneyPipe);
}

describe('MoneyPipe', () => {
  // "Hide amounts" is remembered in localStorage, which other spec files may have left set.
  beforeEach(() => localStorage.clear());
  afterEach(() => TestBed.resetTestingModule());

  it('formats amounts following the English conventions', () => {
    expect(pipeFor('en').transform(1234.5, 'EUR')).toBe('€1,234.50');
  });

  it('formats amounts following the Spanish conventions', () => {
    const text = pipeFor('es').transform(1234.5, 'EUR').replace(/\s/g, ' ');
    expect(text).toMatch(/^1\.?234,50 €$/);
  });

  it('can show the sign of movements', () => {
    const pipe = pipeFor('en');
    expect(pipe.transform(10, 'USD', true)).toBe('+$10.00');
    expect(pipe.transform(-10, 'USD', true)).toBe('-$10.00');
    expect(pipe.transform(0, 'USD', true)).toBe('$0.00');
  });

  it('renders nothing for missing values', () => {
    expect(pipeFor('en').transform(null, 'EUR')).toBe('');
  });
});
