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

  it('posts settlements with their scope', () => {
    api.settle({ scope: 'Debt', debtId: 'd1', walletId: 'w1' }).subscribe();

    const request = http.expectOne('/api/debts/settlements');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ scope: 'Debt', debtId: 'd1', walletId: 'w1' });
    request.flush({ id: 'op', kind: 'Settlement', status: 'Pending' });
  });
});
