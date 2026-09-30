// Mirrors of the Ledger HTTP API contracts (JSON, camelCase, enums as strings).

export type Currency = 'EUR' | 'USD' | 'ARS';
export const CURRENCIES: readonly Currency[] = ['EUR', 'USD', 'ARS'];

export interface Me {
  id: string;
  personId: string;
  email: string;
  displayName: string;
}

export type WalletStatus = 'Active' | 'Archived';

export interface Wallet {
  id: string;
  name: string;
  currency: Currency;
  balance: number;
  status: WalletStatus;
  createdAt: string;
  isReady: boolean;
}

export type CategoryType = 'Income' | 'Expense';

export interface Category {
  id: string;
  name: string;
  type: CategoryType;
  isArchived: boolean;
}

export type ContactOrigin = 'Manual' | 'Mirrored';

export interface Contact {
  id: string;
  personId: string;
  name: string;
  email: string | null;
  linkedUserId: string | null;
  isLinked: boolean;
  origin: ContactOrigin;
  isInUse: boolean;
}

export interface GroupMember {
  personId: string;
  displayName: string;
  userId: string | null;
  isMe: boolean;
  hasActivity: boolean;
}

export interface Group {
  id: string;
  name: string;
  createdBy: string;
  createdAt: string;
  members: GroupMember[];
}

export type TransactionKind =
  | 'OpeningBalance'
  | 'Income'
  | 'Expense'
  | 'Transfer'
  | 'Loan'
  | 'Split'
  | 'Settlement'
  | 'WalletAssignment';

export type AccountPurpose =
  | 'Wallet'
  | 'Category'
  | 'Receivables'
  | 'Payables'
  | 'OpeningBalances'
  | 'CurrencyExchange'
  | 'Uncategorized'
  | 'UncategorizedIncome'
  | 'PendingSettlements';

export interface Posting {
  accountId: string;
  accountName: string;
  accountPurpose: AccountPurpose | '';
  walletId: string | null;
  amount: number;
  currency: Currency;
  counterpartyId: string | null;
  counterpartyName: string | null;
}

export interface Transaction {
  id: string;
  occurredAt: string;
  description: string | null;
  kind: TransactionKind;
  correlationId: string | null;
  postings: Posting[];
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface TransactionFilter {
  page: number;
  pageSize: number;
  walletId?: string | null;
  categoryId?: string | null;
  contactId?: string | null;
  from?: string | null;
  to?: string | null;
}

export interface CurrencySummary {
  currency: Currency;
  wallets: number;
  owedToMe: number;
  iOwe: number;
  pending: number;
  net: number;
}

export interface WalletBalance {
  walletId: string;
  name: string;
  currency: Currency;
  balance: number;
}

export interface CounterpartyBalance {
  counterpartyId: string;
  name: string;
  kind: 'Contact' | 'Group';
  currency: Currency;
  owedToMe: number;
  iOwe: number;
  net: number;
}

export interface Summary {
  totals: CurrencySummary[];
  wallets: WalletBalance[];
  counterparties: CounterpartyBalance[];
}

export interface PendingAssignment {
  id: string;
  operationId: string;
  /** Positive: money I received. Negative: money I paid. */
  amount: number;
  currency: Currency;
  counterpartyId: string | null;
  counterpartyName: string | null;
  description: string | null;
  occurredAt: string;
}

export type OperationKind = 'Loan' | 'Split' | 'Settlement';
export type OperationStatus = 'Pending' | 'Completed' | 'Rejected';

export interface Operation {
  id: string;
  kind: OperationKind;
  status: OperationStatus;
  rejectionReason: string | null;
  createdAt: string;
  completedAt: string | null;
}

export type DebtDirection = 'OwedToMe' | 'IOwe';
export type DebtStatus = 'Pending' | 'Open' | 'Settled' | 'Rejected';

export interface Debt {
  id: string;
  operationId: string;
  groupId: string | null;
  groupName: string | null;
  counterpartyPersonId: string;
  counterpartyName: string;
  direction: DebtDirection;
  amount: number;
  currency: Currency;
  status: DebtStatus;
  isSettling: boolean;
  description: string | null;
  createdAt: string;
}

export interface ContactDebts {
  personId: string;
  name: string;
  currency: Currency;
  owedToMe: number;
  iOwe: number;
  net: number;
}

export interface MyDebts {
  debts: Debt[];
  byContact: ContactDebts[];
}

export interface MemberBalance {
  personId: string;
  name: string;
  currency: Currency;
  balance: number;
}

export interface SuggestedPayment {
  fromPersonId: string;
  fromName: string;
  toPersonId: string;
  toName: string;
  amount: number;
  currency: Currency;
}

export interface GroupBalances {
  groupId: string;
  groupName: string;
  balances: MemberBalance[];
  suggestedPayments: SuggestedPayment[];
  debts: Debt[];
}

export type SplitMethod = 'Equal' | 'Custom' | 'Percent';

export interface SplitPayer {
  personId: string;
  amount: number;
  walletId?: string | null;
}

export interface SplitParticipant {
  personId: string;
  value?: number | null;
}

export interface SplitRequest {
  groupId?: string | null;
  total: number;
  currency: Currency;
  method: SplitMethod;
  payers: SplitPayer[];
  participants: SplitParticipant[];
  categoryId?: string | null;
  description?: string | null;
}

export type SettleRequest =
  | { scope: 'Debt'; debtId: string; walletId: string }
  | { scope: 'Contact'; contactId: string; currency: Currency; walletId: string | null }
  | {
      scope: 'GroupPair';
      groupId: string;
      fromPersonId: string;
      toPersonId: string;
      currency: Currency;
      walletId: string | null;
    };

/** RFC 7807 problem details returned by the API. */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
}
