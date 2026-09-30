import { HttpErrorResponse } from '@angular/common/http';
import { errorMessage } from './error-messages';

describe('errorMessage', () => {
  it('translates known business rule codes', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { code: 'ledger.insufficient_funds', detail: 'Wallet X does not have enough money (…)' },
    });

    expect(errorMessage(error)).toBe('The wallet does not have enough money.');
  });

  it('joins validation errors', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { errors: { Name: ["'Name' must not be empty."], Amount: ['Too big.'] } },
    });

    expect(errorMessage(error)).toBe("'Name' must not be empty. Too big.");
  });

  it('explains when the API cannot be reached', () => {
    expect(errorMessage(new HttpErrorResponse({ status: 0 }))).toContain('not reachable');
  });

  it('falls back to the message sent by the API', () => {
    const error = new HttpErrorResponse({ status: 400, error: { code: 'other.rule', detail: 'Some rule.' } });
    expect(errorMessage(error)).toBe('Some rule.');
  });
});
