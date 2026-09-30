import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Category,
  CategoryType,
  Contact,
  Currency,
  Group,
  GroupBalances,
  Me,
  MyDebts,
  Operation,
  PagedResult,
  PendingAssignment,
  SettleRequest,
  SplitRequest,
  Summary,
  Transaction,
  TransactionFilter,
  Wallet,
} from './models';

/** Typed client of the Ledger HTTP API. Requests go to /api (proxied to the API). */
@Injectable({ providedIn: 'root' })
export class LedgerApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  me(): Observable<Me> {
    return this.http.get<Me>(`${this.base}/me`);
  }

  summary(): Observable<Summary> {
    return this.http.get<Summary>(`${this.base}/summary`);
  }

  // Wallets
  wallets(includeArchived = false): Observable<Wallet[]> {
    return this.http.get<Wallet[]>(`${this.base}/wallets`, { params: { includeArchived } });
  }

  createWallet(name: string, currency: Currency, openingBalance: number): Observable<Wallet> {
    return this.http.post<Wallet>(`${this.base}/wallets`, { name, currency, openingBalance });
  }

  renameWallet(id: string, name: string): Observable<void> {
    return this.http.patch<void>(`${this.base}/wallets/${id}`, { name });
  }

  archiveWallet(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/wallets/${id}/archive`, null);
  }

  deleteWallet(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/wallets/${id}`);
  }

  // Categories
  categories(includeArchived = false): Observable<Category[]> {
    return this.http.get<Category[]>(`${this.base}/categories`, { params: { includeArchived } });
  }

  createCategory(name: string, type: CategoryType): Observable<Category> {
    return this.http.post<Category>(`${this.base}/categories`, { name, type });
  }

  renameCategory(id: string, name: string): Observable<void> {
    return this.http.patch<void>(`${this.base}/categories/${id}`, { name });
  }

  archiveCategory(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/categories/${id}/archive`, null);
  }

  // Transactions
  transactions(filter: TransactionFilter): Observable<PagedResult<Transaction>> {
    let params = new HttpParams().set('page', filter.page).set('pageSize', filter.pageSize);

    for (const key of ['walletId', 'categoryId', 'contactId', 'from', 'to'] as const) {
      const value = filter[key];
      if (value) params = params.set(key, value);
    }

    return this.http.get<PagedResult<Transaction>>(`${this.base}/transactions`, { params });
  }

  recordIncome(body: { walletId: string; amount: number; categoryId?: string | null; description?: string | null }) {
    return this.http.post<{ id: string }>(`${this.base}/transactions/income`, body);
  }

  recordExpense(body: { walletId: string; amount: number; categoryId?: string | null; description?: string | null }) {
    return this.http.post<{ id: string }>(`${this.base}/transactions/expense`, body);
  }

  transfer(body: {
    fromWalletId: string;
    toWalletId: string;
    amount: number;
    receivedAmount?: number | null;
    description?: string | null;
  }) {
    return this.http.post<{ id: string }>(`${this.base}/transactions/transfer`, body);
  }

  // Pending wallet assignments
  pendingAssignments(): Observable<PendingAssignment[]> {
    return this.http.get<PendingAssignment[]>(`${this.base}/pending-assignments`);
  }

  assignWallet(id: string, walletId: string) {
    return this.http.post<{ transactionId: string }>(`${this.base}/pending-assignments/${id}/assign`, { walletId });
  }

  // Contacts
  contacts(): Observable<Contact[]> {
    return this.http.get<Contact[]>(`${this.base}/contacts`);
  }

  createContact(name: string, email: string | null): Observable<Contact> {
    return this.http.post<Contact>(`${this.base}/contacts`, { name, email });
  }

  linkContact(id: string, email: string): Observable<Contact> {
    return this.http.post<Contact>(`${this.base}/contacts/${id}/link`, { email });
  }

  // Groups
  groups(): Observable<Group[]> {
    return this.http.get<Group[]>(`${this.base}/groups`);
  }

  group(id: string): Observable<Group> {
    return this.http.get<Group>(`${this.base}/groups/${id}`);
  }

  createGroup(name: string, contactIds: string[]): Observable<Group> {
    return this.http.post<Group>(`${this.base}/groups`, { name, contactIds });
  }

  addGroupMember(groupId: string, contactId: string): Observable<Group> {
    return this.http.post<Group>(`${this.base}/groups/${groupId}/members`, { contactId });
  }

  removeGroupMember(groupId: string, personId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/groups/${groupId}/members/${personId}`);
  }

  groupBalances(groupId: string): Observable<GroupBalances> {
    return this.http.get<GroupBalances>(`${this.base}/groups/${groupId}/balances`);
  }

  // Debts (asynchronous operations answer 202 with an Operation)
  debts(includeSettled = false): Observable<MyDebts> {
    return this.http.get<MyDebts>(`${this.base}/debts`, { params: { includeSettled } });
  }

  lend(body: { contactId: string; walletId: string; amount: number; description?: string | null }) {
    return this.http.post<Operation>(`${this.base}/debts/loans`, body);
  }

  split(body: SplitRequest): Observable<Operation> {
    return this.http.post<Operation>(`${this.base}/debts/splits`, body);
  }

  settle(body: SettleRequest): Observable<Operation> {
    return this.http.post<Operation>(`${this.base}/debts/settlements`, body);
  }

  operation(id: string): Observable<Operation> {
    return this.http.get<Operation>(`${this.base}/debts/operations/${id}`);
  }
}
