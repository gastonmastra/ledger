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
  /** A link request waiting for the user's answer (only for contacts already used in debts or groups). */
  pendingLinkRequestId: string | null;
}

export type LinkRequestStatus = 'Pending' | 'Applying' | 'Applied' | 'Rejected' | 'Cancelled' | 'Failed';

/** Linking a contact that already has debts or groups: the user must accept it. */
export interface LinkRequest {
  id: string;
  /** Incoming: somebody wants to link one of their contacts to me. Outgoing: I asked. */
  direction: 'Incoming' | 'Outgoing';
  contactId: string;
  contactName: string;
  ownerId: string;
  ownerName: string;
  targetUserId: string;
  targetName: string;
  status: LinkRequestStatus;
  failureReason: string | null;
  createdAt: string;
  answeredAt: string | null;
}

export interface LinkPreviewBalance {
  counterpartyId: string;
  name: string;
  kind: 'Person' | 'Group';
  currency: Currency;
  /** Positive: owed to the user; negative: the user owes it. */
  amount: number;
}

/** What accepting a link request copies into the user's books. */
export interface LinkPreview {
  requestId: string;
  operations: number;
  balances: LinkPreviewBalance[];
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
  | 'WalletAssignment'
  | 'OpeningBalanceReversal'
  | 'BalanceAdjustment'
  | 'InitialDebt'
  | 'Reversal'
  | 'InvestmentBuy'
  | 'InvestmentSell'
  | 'InvestmentOpening';

export type AccountPurpose =
  | 'Wallet'
  | 'Category'
  | 'Receivables'
  | 'Payables'
  | 'OpeningBalances'
  | 'CurrencyExchange'
  | 'Uncategorized'
  | 'UncategorizedIncome'
  | 'PendingSettlements'
  | 'BalanceAdjustments'
  | 'Investment'
  | 'InvestmentGains';

export interface Posting {
  accountId: string;
  accountName: string;
  accountPurpose: AccountPurpose | '';
  walletId: string | null;
  amount: number;
  currency: Currency;
  counterpartyId: string | null;
  counterpartyName: string | null;
  /** Set on category postings. */
  categoryId: string | null;
}

/** Current: the version that counts. Changed/Deleted: replaced or deleted. Reversal: cancels another one. */
export type TransactionStatus = 'Current' | 'Changed' | 'Deleted' | 'Reversal';

export interface Transaction {
  id: string;
  /** Stable id of the entry: every version of a transaction shares it. */
  entryId: string;
  revision: number;
  occurredAt: string;
  description: string | null;
  kind: TransactionKind;
  status: TransactionStatus;
  correlationId: string | null;
  changeReason: string | null;
  /** Income, expense or transfer that can be edited here. */
  editable: boolean;
  deletable: boolean;
  postings: Posting[];
}

export type ChangeAction = 'Edit' | 'Delete' | 'Restore' | 'System';

export interface TransactionHistoryItem {
  id: string;
  revision: number;
  kind: TransactionKind;
  status: TransactionStatus;
  occurredAt: string;
  recordedAt: string;
  description: string | null;
  action: ChangeAction | null;
  reason: string | null;
  changedByUserId: string | null;
  changedByMe: boolean;
  changedByName: string | null;
  postings: Posting[];
}

export interface TransactionActivity {
  transactionId: string;
  entryId: string;
  kind: TransactionKind;
  action: ChangeAction;
  description: string | null;
  reason: string;
  at: string;
  changedByMe: boolean;
  changedByName: string | null;
}

export interface ReviseTransactionRequest {
  walletId: string;
  amount: number;
  occurredAt: string;
  reason: string;
  toWalletId?: string | null;
  receivedAmount?: number | null;
  categoryId?: string | null;
  description?: string | null;
}

export interface BooksLock {
  lockedUntil: string | null;
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
  includeHistory?: boolean;
}

export interface CurrencySummary {
  currency: Currency;
  wallets: number;
  owedToMe: number;
  iOwe: number;
  pending: number;
  net: number;
  /** Net effect of balance adjustments on the wallets (already included in `wallets`). */
  adjustments: number;
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

export type OperationKind = 'Loan' | 'Split' | 'Settlement' | 'InitialDebt';
export type OperationStatus = 'Pending' | 'Completed' | 'Rejected' | 'Superseded' | 'Deleted';

export interface Operation {
  id: string;
  kind: OperationKind;
  status: OperationStatus;
  rejectionReason: string | null;
  createdAt: string;
  completedAt: string | null;
}

export type DebtDirection = 'OwedToMe' | 'IOwe';
export type DebtStatus = 'Pending' | 'Open' | 'Settled' | 'Rejected' | 'Superseded';

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
  /** A payment (or a group settlement) still being recorded is paying it. */
  isSettling: boolean;
  /** Paid so far (the whole amount once settled). Remaining: what is still owed. */
  paid: number;
  remaining: number;
  isPartiallyPaid: boolean;
  /** When the loan, split or existing debt that created it happened. */
  occurredAt: string;
  description: string | null;
  createdAt: string;
  /** A change of its operation is waiting for confirmation. */
  pendingChange: boolean;
  /** Somebody rejected the last change: their books keep the previous version. */
  disputed: boolean;
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

export interface OpeningBalance {
  amount: number;
  occurredAt: string | null;
}

export type InitialDebtDirection = 'IOwe' | 'TheyOweMe';

export interface InitialDebtRequest {
  contactId: string;
  direction: InitialDebtDirection;
  amount: number;
  currency: Currency;
  occurredAt?: string | null;
  description?: string | null;
}

export type ReportGranularity = 'Day' | 'Month';

export interface ReportBucket {
  start: string;
  income: number;
  expenses: number;
  /** wallets + owedToMe − iOwe, at the end of the bucket. */
  netWorth: number;
  /** Money in wallets (pending settlements included). */
  wallets: number;
  owedToMe: number;
  /** What I owe (positive). */
  iOwe: number;
}

export interface CategoryAmount {
  accountId: string;
  name: string;
  purpose: AccountPurpose;
  amount: number;
}

export interface ReportWallet {
  walletId: string;
  name: string;
  balance: number;
}

export interface Report {
  currency: Currency;
  currencies: Currency[];
  granularity: ReportGranularity;
  totalIncome: number;
  totalExpenses: number;
  buckets: ReportBucket[];
  spendingByCategory: CategoryAmount[];
  walletBalances: ReportWallet[];
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
  occurredAt?: string | null;
}

/** One suggested payment of a group's simplified balances. */
export interface SettleRequest {
  groupId: string;
  fromPersonId: string;
  toPersonId: string;
  currency: Currency;
  walletId: string | null;
  occurredAt?: string | null;
}

/** Debt and Contact: full settlements recorded before partial payments. Payment: debts outside groups. */
export type SettlementScope = 'Debt' | 'Contact' | 'GroupPair' | 'Payment';

export type PaymentDirection = 'TheyPaidMe' | 'IPaidThem';

/** Money paid between me and a contact (outside groups): it pays our debts oldest first. */
export interface RecordPaymentRequest {
  contactId: string;
  currency: Currency;
  direction: PaymentDirection;
  amount: number;
  walletId: string;
  occurredAt?: string | null;
  description?: string | null;
}

/** A new payment (contactId, currency, direction) or an edit of one (operationId). */
export interface PreviewPaymentRequest {
  amount: number;
  contactId?: string;
  currency?: Currency;
  direction?: PaymentDirection;
  operationId?: string;
}

export interface PaymentPreviewLine {
  debtId: string;
  operationId: string;
  description: string | null;
  occurredAt: string;
  direction: DebtDirection;
  debtAmount: number;
  remainingBefore: number;
  paid: number;
  remainingAfter: number;
}

/** Balances: positive when they owe me. Extra: paid beyond what was owed (a new debt). */
export interface PaymentPreview {
  lines: PaymentPreviewLine[];
  extra: number;
  extraDirection: DebtDirection | null;
  balanceBefore: number;
  balanceAfter: number;
}

/** A debt a payment paid (part of). */
export interface PaymentAllocation {
  debtId: string;
  operationId: string;
  description: string | null;
  occurredAt: string;
  direction: DebtDirection;
  debtAmount: number;
  amount: number;
}

/** A payment that paid (part of) a debt. */
export interface DebtPayment {
  settlementOperationId: string;
  entryId: string;
  status: OperationStatus;
  occurredAt: string;
  description: string | null;
  amount: number;
}
export type ChangeKind = 'Revise' | 'Delete' | 'Restore';
export type ChangeStatus = 'Pending' | 'Applying' | 'Applied' | 'Failed';
export type ApprovalStatus = 'Pending' | 'Accepted' | 'Rejected';

export interface PersonAmount {
  personId: string;
  name: string;
  amount: number;
  /** Only my own wallet, and only if I recorded the operation. */
  walletId: string | null;
}

export interface PendingChange {
  changeId: string;
  kind: ChangeKind;
  requestedByName: string;
  requestedByMe: boolean;
  reason: string;
  waitingFor: string[];
  waitingForMe: boolean;
}

/**
 * A loan, existing debt, split or settlement from my point of view. contactId, walletId and
 * categoryId are only set if I recorded it (they are mine).
 */
export interface OperationDetails extends Operation {
  entryId: string;
  revision: number;
  occurredAt: string;
  description: string | null;
  currency: Currency;
  total: number;
  groupId: string | null;
  groupName: string | null;
  method: SplitMethod | null;
  scope: SettlementScope | null;
  recordedByMe: boolean;
  recordedByName: string;
  fromPersonId: string | null;
  fromName: string | null;
  toPersonId: string | null;
  toName: string | null;
  counterpartyPersonId: string | null;
  counterpartyName: string | null;
  contactId: string | null;
  direction: InitialDebtDirection | null;
  walletId: string | null;
  categoryId: string | null;
  payers: PersonAmount[];
  shares: PersonAmount[];
  isDisputed: boolean;
  pendingChange: PendingChange | null;
  canEdit: boolean;
  canDelete: boolean;
  canRestore: boolean;
  /** Why it cannot change now (an error code). */
  blockedReason: string | null;
  /** Payments (or settlements) to change or undo before changing it. */
  settlementOperationIds: string[];
  /** Payments: the debts it paid and how much of each. */
  allocations: PaymentAllocation[] | null;
  /** Payments: paid beyond what was owed (it became a debt of the payee to the payer). */
  extraAmount: number;
}

export interface OperationFilter {
  kind?: OperationKind | null;
  groupId?: string | null;
  personId?: string | null;
  includeHistory?: boolean;
  page: number;
  pageSize: number;
}

export interface ChangeSnapshot {
  total: number;
  currency: Currency;
  occurredAt: string;
  description: string | null;
  fromName: string | null;
  toName: string | null;
  payers: PersonAmount[];
  shares: PersonAmount[];
}

export interface Approval {
  userId: string;
  name: string;
  status: ApprovalStatus;
  reason: string | null;
  respondedAt: string | null;
}

export interface OperationChange {
  id: string;
  operationId: string;
  entryId: string;
  operationKind: OperationKind;
  kind: ChangeKind;
  status: ChangeStatus;
  requestedByName: string;
  requestedByMe: boolean;
  reason: string;
  createdAt: string;
  appliedAt: string | null;
  failureReason: string | null;
  newOperationId: string | null;
  approvals: Approval[];
  myApproval: ApprovalStatus | null;
  disputed: boolean;
  current: ChangeSnapshot;
  /** Null for a delete. */
  proposed: ChangeSnapshot | null;
}

export interface OperationHistory {
  versions: OperationDetails[];
  changes: OperationChange[];
}

export interface ChangeResult {
  changeId: string;
  operationId: string;
  kind: ChangeKind;
  status: ChangeStatus;
  newOperationId: string | null;
  /** Names of the people who still have to answer. */
  waitingFor: string[];
}

/** New values of an operation (every field of its kind is sent again) and why it changes. */
export interface ReviseOperationRequest {
  reason: string;
  occurredAt: string;
  description?: string | null;
  amount?: number | null;
  currency?: Currency | null;
  contactId?: string | null;
  direction?: InitialDebtDirection | null;
  walletId?: string | null;
  method?: SplitMethod | null;
  payers?: SplitPayer[] | null;
  participants?: SplitParticipant[] | null;
  categoryId?: string | null;
}

/** RFC 7807 problem details returned by the API. */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
}

// Investments

export type AssetType = 'Stock' | 'Etf' | 'Crypto' | 'Bond' | 'Fund' | 'Other';
export const ASSET_TYPES: readonly AssetType[] = ['Stock', 'Etf', 'Crypto', 'Bond', 'Fund', 'Other'];
export type AssetStatus = 'Active' | 'Archived';
export type ValuationSource = 'Sell' | 'Buy' | 'Cost';
export type TradeKind = 'Buy' | 'Sell' | 'Opening';
export type TradeStatus = 'Pending' | 'Completed' | 'Rejected';
export type PriceSide = 'Buy' | 'Sell';
export type PriceSource = 'Trade' | 'Manual';

/** An asset and my holding of it, valued now. */
export interface Asset {
  id: string;
  name: string;
  symbol: string | null;
  type: AssetType;
  currency: Currency;
  status: AssetStatus;
  quantity: number;
  costBasis: number;
  averageCost: number | null;
  buyPrice: number | null;
  sellPrice: number | null;
  /** Price used for the market value: the latest sell price, else the latest buy price, else the average cost. */
  valuationPrice: number | null;
  valuationSource: ValuationSource | null;
  marketValue: number;
  unrealizedGain: number;
  realizedGain: number;
  lastTradeAt: string | null;
  hasPendingTrade: boolean;
  createdAt: string;
}

export interface Trade {
  id: string;
  assetId: string;
  assetName: string;
  assetSymbol: string | null;
  kind: TradeKind;
  quantity: number;
  unitPrice: number;
  total: number;
  currency: Currency;
  walletId: string | null;
  walletCurrency: Currency | null;
  walletAmount: number | null;
  costRemoved: number;
  realizedGain: number;
  counterPrice: number | null;
  occurredAt: string;
  description: string | null;
  status: TradeStatus;
  rejectionReason: string | null;
  createdAt: string;
}

export interface AssetPrice {
  id: string;
  side: PriceSide;
  price: number;
  occurredAt: string;
  source: PriceSource;
  tradeId: string | null;
}

export interface InvestmentsTotal {
  currency: Currency;
  value: number;
  cost: number;
  unrealizedGain: number;
  realizedGain: number;
}

export interface InvestmentsBucket {
  start: string;
  value: number;
  cost: number;
}

export interface InvestmentsReport {
  currency: Currency;
  currencies: Currency[];
  granularity: ReportGranularity;
  buckets: InvestmentsBucket[];
  assetValues: { assetId: string; name: string; symbol: string | null; value: number }[];
  realizedGain: number;
}

export interface CreateAssetRequest {
  name: string;
  symbol?: string | null;
  type: AssetType;
  currency: Currency;
}

/** Buy, sell or already owned holding. `walletAmount` is required when the wallet's currency differs from the asset's. */
export interface TradeRequest {
  assetId: string;
  quantity: number;
  unitPrice: number;
  walletId?: string | null;
  walletAmount?: number | null;
  /** Buy and opening: the current sell price. Sell: use `buyPrice`. */
  sellPrice?: number | null;
  buyPrice?: number | null;
  occurredAt?: string | null;
  description?: string | null;
}
