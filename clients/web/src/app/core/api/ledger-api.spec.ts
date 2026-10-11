import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LedgerApi } from './ledger-api';

describe('LedgerApi', () => {
  let api: LedgerApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(LedgerApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends only the filters that are set', () => {
    api.transactions({ page: 2, pageSize: 15, walletId: 'w1', categoryId: null, contactId: '' }).subscribe();

    const request = http.expectOne((r) => r.url === '/api/transactions');
    expect(request.request.params.keys().sort()).toEqual(['page', 'pageSize', 'walletId']);
    expect(request.request.params.get('page')).toBe('2');
    request.flush({ items: [], page: 2, pageSize: 15, totalCount: 0, totalPages: 0 });
  });

  it('posts payments, previews them and reads the payments of a debt', () => {
    const payment = {
      contactId: 'c1', currency: 'EUR' as const, direction: 'TheyPaidMe' as const, amount: 170, walletId: 'w1',
    };
    api.recordPayment(payment).subscribe();
    const post = http.expectOne('/api/debts/payments');
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual(payment);
    post.flush({ id: 'op', kind: 'Settlement', status: 'Pending' });

    api.previewPayment({ amount: 130, operationId: 'op' }).subscribe();
    const preview = http.expectOne('/api/debts/payments/preview');
    expect(preview.request.body).toEqual({ amount: 130, operationId: 'op' });
    preview.flush({ lines: [], extra: 0, extraDirection: null, balanceBefore: 0, balanceAfter: 0 });

    api.debtPayments('d1').subscribe();
    expect(http.expectOne('/api/debts/d1/payments').request.method).toBe('GET');
  });

  it('posts group settlements', () => {
    const body = { groupId: 'g1', fromPersonId: 'p1', toPersonId: 'p2', currency: 'EUR' as const, walletId: 'w1' };
    api.settle(body).subscribe();

    const request = http.expectOne('/api/debts/settlements');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(body);
    request.flush({ id: 'op', kind: 'Settlement', status: 'Pending' });
  });

  it('answers and cancels link requests, and asks Debts for the preview', () => {
    api.linkRequests(true).subscribe();
    const list = http.expectOne((r) => r.url === '/api/contacts/link-requests');
    expect(list.request.params.get('includeClosed')).toBe('true');
    list.flush([]);

    api.acceptLinkRequest('r1').subscribe();
    expect(http.expectOne('/api/contacts/link-requests/r1/accept').request.method).toBe('POST');

    api.cancelLinkRequest('r1').subscribe();
    expect(http.expectOne('/api/contacts/link-requests/r1').request.method).toBe('DELETE');

    api.linkPreview('r1').subscribe();
    expect(http.expectOne('/api/debts/link-requests/r1/preview').request.method).toBe('GET');
  });
});
