import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('dark');
  });

  it('cycles system → light → dark → system and remembers the choice', async () => {
    const theme = TestBed.inject(ThemeService);
    expect(theme.mode()).toBe('system');

    theme.cycle();
    await TestBed.inject(ApplicationRef).whenStable();
    expect(theme.mode()).toBe('light');
    expect(document.documentElement.classList.contains('dark')).toBe(false);

    theme.cycle();
    await TestBed.inject(ApplicationRef).whenStable();
    expect(theme.mode()).toBe('dark');
    expect(document.documentElement.classList.contains('dark')).toBe(true);
    expect(localStorage.getItem('ledger.theme')).toBe('dark');

    theme.cycle();
    expect(theme.mode()).toBe('system');
  });
});
