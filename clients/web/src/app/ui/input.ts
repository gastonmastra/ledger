import { Directive } from '@angular/core';

/** Shared look of text inputs, selects and textareas: `<input uiInput>`. */
@Directive({
  selector: 'input[uiInput], select[uiInput], textarea[uiInput]',
  host: {
    class:
      'block w-full rounded-ui border border-border bg-surface px-3 py-2 text-sm text-foreground ' +
      'placeholder:text-muted focus:border-primary focus:outline-none focus:ring-2 focus:ring-ring/30 ' +
      'disabled:opacity-50 aria-invalid:border-danger',
  },
})
export class Input {}
