import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Button, ButtonVariant } from './button';

@Component({
  imports: [Button],
  template: `<button uiButton [variant]="variant()" [loading]="loading()">Save</button>`,
})
class Host {
  readonly variant = signal<ButtonVariant>('primary');
  readonly loading = signal(false);
}

describe('Button', () => {
  it('uses the semantic color tokens of its variant', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button');

    expect(button.className).toContain('bg-primary');

    fixture.componentInstance.variant.set('danger');
    await fixture.whenStable();
    expect(button.className).toContain('bg-danger');
    expect(button.className).not.toContain('bg-primary ');
  });

  it('is disabled and shows a spinner while loading', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.loading.set(true);
    await fixture.whenStable();
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button');

    expect(button.disabled).toBe(true);
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(button.querySelector('ui-spinner')).not.toBeNull();
  });
});
