import { TestBed } from '@angular/core/testing';
import { PricePipe, QuantityPipe, tradeTotal } from './formatting';

describe('investment formatting', () => {
  it('rounds the trade total to the cent', () => {
    expect(tradeTotal(3, 33.333)).toBe(100);
    expect(tradeTotal(1_000_000, 0.00001234)).toBe(12.34);
    expect(tradeTotal(null, 10)).toBe(0);
  });

  it('shows sub-cent prices and fractional quantities', () => {
    TestBed.runInInjectionContext(() => {
      expect(new PricePipe().transform(0.00001234, 'USD')).toBe('$0.00001234');
      expect(new PricePipe().transform(100, 'USD')).toBe('$100.00');
      expect(new QuantityPipe().transform(0.123456789012)).toBe('0.123456789012');
      expect(new QuantityPipe().transform(2)).toBe('2');
    });
  });
});
