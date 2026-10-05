import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Asset,
  AssetPrice,
  BooksLock,
  CreateAssetRequest,
  InvestmentsReport,
  InvestmentsTotal,
  LinkPreview,
  LinkRequest,
  Category,
  ChangeResult,
  CategoryType,
  Contact,
  Currency,
  Group,
  GroupBalances,
  InitialDebtRequest,
  Me,
  MyDebts,
  Operation,
  OperationChange,
  OperationDetails,
  OperationFilter,
  OperationHistory,
  OpeningBalance,
  PagedResult,
  PendingAssignment,
  Report,
  ReviseOperationRequest,
  ReviseTransactionRequest,
  SettleRequest,
  SplitRequest,
  Summary,
  Trade,
  TradeKind,
  TradeRequest,
  Transaction,
  TransactionActivity,
  TransactionFilter,
  TransactionHistoryItem,
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

  createWallet(name: string, currency: Currency, openingBalance: number, openingBalanceAt?: string | null): Observable<Wallet> {
    return this.http.post<Wallet>(`${this.base}/wallets`, { name, currency, openingBalance, openingBalanceAt });
  }

  openingBalance(walletId: string): Observable<OpeningBalance> {
    return this.http.get<OpeningBalance>(`${this.base}/transactions/opening-balance/${walletId}`);
  }

  changeOpeningBalance(walletId: string, amount: number, occurredAt: string, reason: string): Observable<void> {
    return this.http.put<void>(`${this.base}/transactions/opening-balance`, { walletId, amount, occurredAt, reason });
  }

  adjustBalance(body: { walletId: string; actualBalance: number; occurredAt: string; description?: string | null }) {
    return this.http.post<{ id: string }>(`${this.base}/transactions/balance-adjustment`, body);
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

    if (filter.includeHistory) params = params.set('includeHistory', true);

    return this.http.get<PagedResult<Transaction>>(`${this.base}/transactions`, { params });
  }

  /** Posts a reversal of the current version and the corrected one. Returns the new version's id. */
  reviseTransaction(id: string, body: ReviseTransactionRequest) {
    return this.http.put<{ id: string }>(`${this.base}/transactions/${id}`, body);
  }

  deleteTransaction(id: string, reason: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/transactions/${id}`, { body: { reason } });
  }

  restoreTransaction(id: string, reason: string) {
    return this.http.post<{ id: string }>(`${this.base}/transactions/${id}/restore`, { reason });
  }

  transactionHistory(id: string): Observable<TransactionHistoryItem[]> {
    return this.http.get<TransactionHistoryItem[]>(`${this.base}/transactions/${id}/history`);
  }

  transactionActivity(page = 1, pageSize = 30): Observable<PagedResult<TransactionActivity>> {
    return this.http.get<PagedResult<TransactionActivity>>(`${this.base}/transactions/activity`, {
      params: { page, pageSize },
    });
  }

  // Settings
  booksLock(): Observable<BooksLock> {
    return this.http.get<BooksLock>(`${this.base}/settings/books-lock`);
  }

  setBooksLock(lockedUntil: string | null): Observable<BooksLock> {
    return this.http.put<BooksLock>(`${this.base}/settings/books-lock`, { lockedUntil });
  }

  recordIncome(body: MovementBody) {
    return this.http.post<{ id: string }>(`${this.base}/transactions/income`, body);
  }

  recordExpense(body: MovementBody) {
    return this.http.post<{ id: string }>(`${this.base}/transactions/expense`, body);
  }

  transfer(body: {
    fromWalletId: string;
    toWalletId: string;
    amount: number;
    receivedAmount?: number | null;
    description?: string | null;
    occurredAt?: string | null;
  }) {
    return this.http.post<{ id: string }>(`${this.base}/transactions/transfer`, body);
  }

  // Pending wallet assignments
  pendingAssignments(): Observable<PendingAssignment[]> {
    return this.http.get<PendingAssignment[]>(`${this.base}/pending-assignments`);
  }

  assignWallet(id: string, walletId: string, occurredAt?: string | null) {
    return this.http.post<{ transactionId: string }>(`${this.base}/pending-assignments/${id}/assign`, {
      walletId,
      occurredAt,
    });
  }

  // Contacts
  contacts(): Observable<Contact[]> {
    return this.http.get<Contact[]>(`${this.base}/contacts`);
  }

  createContact(name: string, email: string | null): Observable<Contact> {
    return this.http.post<Contact>(`${this.base}/contacts`, { name, email });
  }

  /** Links right away when the contact is not used yet; otherwise sends a link request (`pendingLinkRequestId`). */
  linkContact(id: string, email: string): Observable<Contact> {
    return this.http.post<Contact>(`${this.base}/contacts/${id}/link`, { email });
  }

  linkRequests(includeClosed = false): Observable<LinkRequest[]> {
    return this.http.get<LinkRequest[]>(`${this.base}/contacts/link-requests`, { params: { includeClosed } });
  }

  acceptLinkRequest(id: string): Observable<LinkRequest> {
    return this.http.post<LinkRequest>(`${this.base}/contacts/link-requests/${id}/accept`, null);
  }

  rejectLinkRequest(id: string): Observable<LinkRequest> {
    return this.http.post<LinkRequest>(`${this.base}/contacts/link-requests/${id}/reject`, null);
  }

  cancelLinkRequest(id: string): Observable<LinkRequest> {
    return this.http.delete<LinkRequest>(`${this.base}/contacts/link-requests/${id}`);
  }

  linkPreview(id: string): Observable<LinkPreview> {
    return this.http.get<LinkPreview>(`${this.base}/debts/link-requests/${id}/preview`);
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

  lend(body: {
    contactId: string;
    walletId: string;
    amount: number;
    description?: string | null;
    occurredAt?: string | null;
  }) {
    return this.http.post<Operation>(`${this.base}/debts/loans`, body);
  }

  recordInitialDebt(body: InitialDebtRequest): Observable<Operation> {
    return this.http.post<Operation>(`${this.base}/debts/initial`, body);
  }

  split(body: SplitRequest): Observable<Operation> {
    return this.http.post<Operation>(`${this.base}/debts/splits`, body);
  }

  settle(body: SettleRequest): Observable<Operation> {
    return this.http.post<Operation>(`${this.base}/debts/settlements`, body);
  }

  operation(id: string): Observable<OperationDetails> {
    return this.http.get<OperationDetails>(`${this.base}/debts/operations/${id}`);
  }

  operations(filter: OperationFilter): Observable<PagedResult<OperationDetails>> {
    let params = new HttpParams().set('page', filter.page).set('pageSize', filter.pageSize);

    for (const key of ['kind', 'groupId', 'personId'] as const) {
      const value = filter[key];
      if (value) params = params.set(key, value);
    }

    if (filter.includeHistory) params = params.set('includeHistory', true);

    return this.http.get<PagedResult<OperationDetails>>(`${this.base}/debts/operations`, { params });
  }

  operationHistory(id: string): Observable<OperationHistory> {
    return this.http.get<OperationHistory>(`${this.base}/debts/operations/${id}/history`);
  }

  /** Linked users involved confirm it first; then the books are updated asynchronously. */
  reviseOperation(id: string, body: ReviseOperationRequest): Observable<ChangeResult> {
    return this.http.put<ChangeResult>(`${this.base}/debts/operations/${id}`, body);
  }

  deleteOperation(id: string, reason: string): Observable<ChangeResult> {
    return this.http.delete<ChangeResult>(`${this.base}/debts/operations/${id}`, { body: { reason } });
  }

  restoreOperation(id: string, reason: string): Observable<ChangeResult> {
    return this.http.post<ChangeResult>(`${this.base}/debts/operations/${id}/restore`, { reason });
  }

  /** filter: ToReview (waiting for my answer) or All. */
  changes(filter: 'ToReview' | 'All' = 'ToReview'): Observable<OperationChange[]> {
    return this.http.get<OperationChange[]>(`${this.base}/debts/changes`, { params: { filter } });
  }

  respondToChange(id: string, accept: boolean, reason?: string | null): Observable<ChangeResult> {
    return this.http.post<ChangeResult>(`${this.base}/debts/changes/${id}/${accept ? 'accept' : 'reject'}`, {
      reason: reason || null,
    });
  }

  debtActivity(page = 1, pageSize = 30): Observable<PagedResult<OperationChange>> {
    return this.http.get<PagedResult<OperationChange>>(`${this.base}/debts/activity`, { params: { page, pageSize } });
  }

  // Reports
  report(from: string, to: string, currency: Currency | null, timeZone: string): Observable<Report> {
    let params = new HttpParams().set('from', from).set('to', to).set('timeZone', timeZone);
    if (currency) params = params.set('currency', currency);

    return this.http.get<Report>(`${this.base}/reports`, { params });
  }

  // Investments
  assets(includeArchived = false): Observable<Asset[]> {
    return this.http.get<Asset[]>(`${this.base}/investments/assets`, { params: { includeArchived } });
  }

  createAsset(body: CreateAssetRequest): Observable<Asset> {
    return this.http.post<Asset>(`${this.base}/investments/assets`, body);
  }

  updateAsset(id: string, name: string, symbol: string | null): Observable<void> {
    return this.http.put<void>(`${this.base}/investments/assets/${id}`, { name, symbol });
  }

  archiveAsset(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/investments/assets/${id}/archive`, null);
  }

  assetPrices(id: string): Observable<AssetPrice[]> {
    return this.http.get<AssetPrice[]>(`${this.base}/investments/assets/${id}/prices`);
  }

  recordPrice(id: string, body: { buyPrice?: number | null; sellPrice?: number | null; occurredAt?: string | null }): Observable<void> {
    return this.http.post<void>(`${this.base}/investments/assets/${id}/prices`, body);
  }

  /** Buy, sell or record an already owned holding (`opening`). Answers 202 with a pending trade. */
  recordTrade(kind: TradeKind, body: TradeRequest): Observable<Trade> {
    const path = kind === 'Buy' ? 'buy' : kind === 'Sell' ? 'sell' : 'opening';
    return this.http.post<Trade>(`${this.base}/investments/trades/${path}`, body);
  }

  trades(assetId: string | null, page = 1, pageSize = 20): Observable<PagedResult<Trade>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (assetId) params = params.set('assetId', assetId);

    return this.http.get<PagedResult<Trade>>(`${this.base}/investments/trades`, { params });
  }

  trade(id: string): Observable<Trade> {
    return this.http.get<Trade>(`${this.base}/investments/trades/${id}`);
  }

  investmentsSummary(): Observable<InvestmentsTotal[]> {
    return this.http.get<InvestmentsTotal[]>(`${this.base}/investments/summary`);
  }

  investmentsReport(from: string, to: string, currency: Currency | null, timeZone: string): Observable<InvestmentsReport> {
    let params = new HttpParams().set('from', from).set('to', to).set('timeZone', timeZone);
    if (currency) params = params.set('currency', currency);

    return this.http.get<InvestmentsReport>(`${this.base}/investments/report`, { params });
  }
}

export interface MovementBody {
  walletId: string;
  amount: number;
  categoryId?: string | null;
  description?: string | null;
  occurredAt?: string | null;
}
