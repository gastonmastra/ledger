import { ChangeDetectionStrategy, Component, forwardRef, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { isoToLocalInput, localInputToIso, toLocalInputValue } from '../core/dates';
import { Input } from './input';

/**
 * Date and time picker in the user's local time. Its form value is an ISO string in UTC
 * (what the API expects). Future dates cannot be picked.
 */
@Component({
  selector: 'ui-datetime-input',
  imports: [Input],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => DateTimeInput), multi: true }],
  host: { class: 'block' },
  template: `
    <input
      uiInput
      type="datetime-local"
      [value]="local()"
      [max]="max()"
      [disabled]="disabled()"
      (focus)="max.set(now())"
      (change)="onChange($event)"
      (blur)="touched()"
    />
  `,
})
export class DateTimeInput implements ControlValueAccessor {
  protected readonly local = signal(toLocalInputValue());
  protected readonly max = signal(toLocalInputValue());
  protected readonly disabled = signal(false);

  private changed: (value: string) => void = () => undefined;
  protected touched: () => void = () => undefined;

  writeValue(value: string | null): void {
    this.local.set(value ? isoToLocalInput(value) : '');
  }

  registerOnChange(fn: (value: string) => void): void {
    this.changed = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.touched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.disabled.set(disabled);
  }

  protected onChange(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.local.set(value);
    this.changed(value ? localInputToIso(value) : '');
  }

  protected now(): string {
    return toLocalInputValue();
  }
}

/** The value a date field starts with: now, in UTC. */
export function nowIso(): string {
  return new Date().toISOString();
}
