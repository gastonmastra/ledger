import { ChangeDetectionStrategy, Component, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { FormField } from './form-field';
import { Input } from './input';

/** Maximum length of a reason (same as the API). */
export const REASON_MAX_LENGTH = 500;

/**
 * "Why?" field required by every edit, deletion and restore: the ledger keeps it in the history.
 * Works with reactive forms and ngModel.
 */
@Component({
  selector: 'ui-reason-field',
  imports: [FormField, Input],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => ReasonField), multi: true }],
  host: { class: 'block' },
  template: `
    <ui-form-field [label]="label() ?? defaultLabel" [hint]="hint() ?? defaultHint">
      <textarea
        uiInput
        rows="2"
        [attr.maxlength]="maxLength"
        [value]="value()"
        [disabled]="disabled()"
        [required]="required()"
        [attr.placeholder]="placeholder"
        (input)="onInput($event)"
        (blur)="touched()"
      ></textarea>
    </ui-form-field>
  `,
})
export class ReasonField implements ControlValueAccessor {
  readonly label = input<string | null>(null);
  readonly hint = input<string | null>(null);
  readonly required = input(true);

  protected readonly maxLength = REASON_MAX_LENGTH;
  protected readonly defaultLabel = $localize`:@@reason.label:Why?`;
  protected readonly defaultHint = $localize`:@@reason.hint:Kept in the history, so everybody involved knows what changed and why.`;
  protected readonly placeholder = $localize`:@@reason.placeholder:E.g. wrong amount, duplicated…`;
  protected readonly value = signal('');
  protected readonly disabled = signal(false);

  private changed: (value: string) => void = () => undefined;
  protected touched: () => void = () => undefined;

  writeValue(value: string | null): void {
    this.value.set(value ?? '');
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

  protected onInput(event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.value.set(value);
    this.changed(value);
  }
}
