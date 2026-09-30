import {
  AccountPurpose,
  CategoryType,
  DebtStatus,
  Posting,
  SplitMethod,
  TransactionKind,
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
