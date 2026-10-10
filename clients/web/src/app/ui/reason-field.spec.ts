import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { REASON_MAX_LENGTH, ReasonField } from './reason-field';

@Component({
  imports: [ReasonField, ReactiveFormsModule],
  template: `<ui-reason-field [formControl]="reason" />`,
})
class Host {
  readonly reason = new FormControl('', { nonNullable: true, validators: Validators.required });
}

describe('ReasonField', () => {
  it('is a form control limited to the length the API accepts', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const textarea: HTMLTextAreaElement = fixture.nativeElement.querySelector('textarea');

    expect(textarea.maxLength).toBe(REASON_MAX_LENGTH);
    expect(fixture.componentInstance.reason.valid).toBe(false);

    textarea.value = 'Wrong amount';
    textarea.dispatchEvent(new Event('input'));

    expect(fixture.componentInstance.reason.value).toBe('Wrong amount');
    expect(fixture.componentInstance.reason.valid).toBe(true);
  });

  it('shows the value set by the form', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.reason.setValue('Duplicated');
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('textarea').value).toBe('Duplicated');
  });
});
