import {
  AccountPurpose,
  ApprovalStatus,
  AssetType,
  CategoryType,
  ChangeAction,
  ChangeKind,
  ChangeStatus,
  DebtStatus,
  LinkRequestStatus,
  OperationKind,
  OperationStatus,
  Posting,
  SplitMethod,
  TransactionKind,
  TradeKind,
  TradeStatus,
  TransactionStatus,
  ValuationSource,
  WalletStatus,
} from '../api/models';

// Localized names for values that come from the API as codes.

export function transactionKindLabel(kind: TransactionKind): string {
  const labels: Record<TransactionKind, () => string> = {
    OpeningBalance: () => $localize`:@@kind.openingBalance:Opening balance`,
    Income: () => $localize`:@@kind.income:Income`,
    Expense: () => $localize`:@@kind.expense:Expense`,
    Transfer: () => $localize`:@@kind.transfer:Transfer`,
    Loan: () => $localize`:@@kind.loan:Loan`,
    Split: () => $localize`:@@kind.split:Split`,
    Settlement: () => $localize`:@@kind.settlement:Settlement`,
    WalletAssignment: () => $localize`:@@kind.walletAssignment:Wallet assigned`,
    OpeningBalanceReversal: () => $localize`:@@kind.openingBalanceReversal:Opening balance replaced`,
    BalanceAdjustment: () => $localize`:@@kind.balanceAdjustment:Balance adjustment`,
    InitialDebt: () => $localize`:@@kind.initialDebt:Existing debt`,
    Reversal: () => $localize`:@@kind.reversal:Reversal`,
    InvestmentBuy: () => $localize`:@@kind.investmentBuy:Investment purchase`,
    InvestmentSell: () => $localize`:@@kind.investmentSell:Investment sale`,
    InvestmentOpening: () => $localize`:@@kind.investmentOpening:Investment already owned`,
  };

  return labels[kind]?.() ?? kind;
}

/** System accounts have fixed English names in the API: show them in the user's language. */
export function accountLabel(posting: Pick<Posting, 'accountName' | 'accountPurpose'>): string {
  const labels: Partial<Record<AccountPurpose, () => string>> = {
    Receivables: () => $localize`:@@account.receivables:Owed to me`,
    Payables: () => $localize`:@@account.payables:I owe`,
    OpeningBalances: () => $localize`:@@account.openingBalances:Opening balances`,
    CurrencyExchange: () => $localize`:@@account.currencyExchange:Currency exchange`,
    Uncategorized: () => $localize`:@@account.uncategorized:Uncategorized`,
    UncategorizedIncome: () => $localize`:@@account.uncategorizedIncome:Uncategorized income`,
    PendingSettlements: () => $localize`:@@account.pendingSettlements:Pending settlements`,
    BalanceAdjustments: () => $localize`:@@account.balanceAdjustments:Balance adjustments`,
    InvestmentGains: () => $localize`:@@account.investmentGains:Investment gains`,
  };

  const label = posting.accountPurpose ? labels[posting.accountPurpose as AccountPurpose] : undefined;
  return label ? label() : posting.accountName;
}

export function categoryTypeLabel(type: CategoryType): string {
  return type === 'Income'
    ? $localize`:@@categoryType.income:Income`
    : $localize`:@@categoryType.expense:Expense`;
}

export function walletStatusLabel(status: WalletStatus): string {
  return status === 'Active'
    ? $localize`:@@walletStatus.active:Active`
    : $localize`:@@walletStatus.archived:Archived`;
}

export function debtStatusLabel(status: DebtStatus, isSettling: boolean): string {
  if (isSettling) return $localize`:@@debtStatus.settling:Settling`;

  const labels: Record<DebtStatus, () => string> = {
    Pending: () => $localize`:@@debtStatus.pending:Pending`,
    Open: () => $localize`:@@debtStatus.open:Open`,
    Settled: () => $localize`:@@debtStatus.settled:Settled`,
    Rejected: () => $localize`:@@debtStatus.rejected:Rejected`,
    Superseded: () => $localize`:@@debtStatus.superseded:Replaced`,
  };

  return labels[status]();
}

export function splitMethodLabel(method: SplitMethod): string {
  const labels: Record<SplitMethod, () => string> = {
    Equal: () => $localize`:@@splitMethod.equal:Equally`,
    Custom: () => $localize`:@@splitMethod.custom:By amount`,
    Percent: () => $localize`:@@splitMethod.percent:By percentage`,
  };

  return labels[method]();
}

export function meLabel(): string {
  return $localize`:@@person.me:Me`;
}

export function transactionStatusLabel(status: TransactionStatus): string {
  const labels: Record<TransactionStatus, () => string> = {
    Current: () => $localize`:@@txStatus.current:Current`,
    Changed: () => $localize`:@@txStatus.changed:Changed`,
    Deleted: () => $localize`:@@txStatus.deleted:Deleted`,
    Reversal: () => $localize`:@@txStatus.reversal:Reversal`,
  };

  return labels[status]();
}

export function changeActionLabel(action: ChangeAction | ChangeKind): string {
  const labels: Record<ChangeAction | ChangeKind, () => string> = {
    Edit: () => $localize`:@@action.edit:Edited`,
    Revise: () => $localize`:@@action.edit:Edited`,
    Delete: () => $localize`:@@action.delete:Deleted`,
    Restore: () => $localize`:@@action.restore:Restored`,
    System: () => $localize`:@@action.system:Changed by Ledger`,
  };

  return labels[action]();
}

export function operationKindLabel(kind: OperationKind): string {
  const labels: Record<OperationKind, () => string> = {
    Loan: () => $localize`:@@kind.loan:Loan`,
    Split: () => $localize`:@@kind.split:Split`,
    Settlement: () => $localize`:@@kind.settlement:Settlement`,
    InitialDebt: () => $localize`:@@kind.initialDebt:Existing debt`,
  };

  return labels[kind]();
}

export function operationStatusLabel(status: OperationStatus): string {
  const labels: Record<OperationStatus, () => string> = {
    Pending: () => $localize`:@@opStatus.pending:Being recorded`,
    Completed: () => $localize`:@@txStatus.current:Current`,
    Rejected: () => $localize`:@@opStatus.rejected:Refused`,
    Superseded: () => $localize`:@@txStatus.changed:Changed`,
    Deleted: () => $localize`:@@txStatus.deleted:Deleted`,
  };

  return labels[status]();
}

export function changeStatusLabel(status: ChangeStatus): string {
  const labels: Record<ChangeStatus, () => string> = {
    Pending: () => $localize`:@@changeStatus.pending:Waiting for confirmation`,
    Applying: () => $localize`:@@changeStatus.applying:Updating the books`,
    Applied: () => $localize`:@@changeStatus.applied:Applied`,
    Failed: () => $localize`:@@changeStatus.failed:Failed`,
  };

  return labels[status]();
}

export function approvalStatusLabel(status: ApprovalStatus): string {
  const labels: Record<ApprovalStatus, () => string> = {
    Pending: () => $localize`:@@approval.pending:Not answered yet`,
    Accepted: () => $localize`:@@approval.accepted:Accepted`,
    Rejected: () => $localize`:@@approval.rejected:Rejected`,
  };

  return labels[status]();
}

export function assetTypeLabel(type: AssetType): string {
  const labels: Record<AssetType, () => string> = {
    Stock: () => $localize`:@@assetType.stock:Stock`,
    Etf: () => $localize`:@@assetType.etf:ETF`,
    Crypto: () => $localize`:@@assetType.crypto:Crypto`,
    Bond: () => $localize`:@@assetType.bond:Bond`,
    Fund: () => $localize`:@@assetType.fund:Fund`,
    Other: () => $localize`:@@assetType.other:Other`,
  };

  return labels[type]();
}

export function tradeKindLabel(kind: TradeKind): string {
  const labels: Record<TradeKind, () => string> = {
    Buy: () => $localize`:@@tradeKind.buy:Buy`,
    Sell: () => $localize`:@@tradeKind.sell:Sell`,
    Opening: () => $localize`:@@tradeKind.opening:Already owned`,
  };

  return labels[kind]();
}

export function tradeStatusLabel(status: TradeStatus): string {
  const labels: Record<TradeStatus, () => string> = {
    Pending: () => $localize`:@@opStatus.pending:Being recorded`,
    Completed: () => $localize`:@@tradeStatus.completed:Recorded`,
    Rejected: () => $localize`:@@opStatus.rejected:Refused`,
  };

  return labels[status]();
}

export function valuationSourceLabel(source: ValuationSource): string {
  const labels: Record<ValuationSource, () => string> = {
    Sell: () => $localize`:@@valuation.sell:Sell price`,
    Buy: () => $localize`:@@valuation.buy:Buy price (no sell price yet)`,
    Cost: () => $localize`:@@valuation.cost:Average cost (no price yet)`,
  };

  return labels[source]();
}

export function priceSideLabel(side: 'Buy' | 'Sell'): string {
  return side === 'Buy' ? $localize`:@@priceSide.buy:Buy price` : $localize`:@@priceSide.sell:Sell price`;
}

export function linkRequestStatusLabel(status: LinkRequestStatus): string {
  const labels: Record<LinkRequestStatus, () => string> = {
    Pending: () => $localize`:@@changeStatus.pending:Waiting for confirmation`,
    Applying: () => $localize`:@@changeStatus.applying:Updating the books`,
    Applied: () => $localize`:@@linkStatus.applied:Linked`,
    Rejected: () => $localize`:@@approval.rejected:Rejected`,
    Cancelled: () => $localize`:@@linkStatus.cancelled:Cancelled`,
    Failed: () => $localize`:@@changeStatus.failed:Failed`,
  };

  return labels[status]();
}
