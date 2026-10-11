import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FilterChip, FilterChips } from './filter-chips';

@Component({
  imports: [FilterChips],
  template: `<ui-filter-chips [chips]="chips()" (remove)="removed = $event" (clear)="cleared = true" />`,
})
class Host {
  readonly chips = signal<FilterChip[]>([]);
  removed = '';
  cleared = false;
}

describe('FilterChips', () => {
  it('shows nothing without filters, and removes one or all', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent.trim()).toBe('');

    fixture.componentInstance.chips.set([
      { key: 'walletId', label: 'Wallet: Bank' },
      { key: 'from', label: 'From: 2026-10-01' },
    ]);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Wallet: Bank');

    const buttons: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll('button'));
    buttons[1].click();
    expect(fixture.componentInstance.removed).toBe('from');
    buttons[2].click();
    expect(fixture.componentInstance.cleared).toBe(true);
  });
});
