import { HttpErrorResponse } from '@angular/common/http';
import { ProblemDetails } from './models';

/**
 * Localized messages for the business rule codes returned by the API (ProblemDetails.code).
 * Codes that are not listed fall back to the (English) message sent by the API.
 */
const byCode: Record<string, () => string> = {
  'ledger.insufficient_funds': () => $localize`:@@error.insufficientFunds:The wallet does not have enough money.`,
  'ledger.currency_mismatch': () => $localize`:@@error.currencyMismatch:The wallet uses a different currency.`,
  'ledger.wallet_archived': () => $localize`:@@error.walletArchived:The wallet is archived.`,
  'ledger.category_archived': () => $localize`:@@error.categoryArchived:The category is archived.`,
  'wallet.not_empty': () => $localize`:@@error.walletNotEmpty:Only wallets with a zero balance can be archived.`,
  'wallet.has_transactions': () =>
    $localize`:@@error.walletHasTransactions:Only wallets without transactions can be deleted. Archive it instead.`,
  'wallet.archived': () => $localize`:@@error.walletIsArchived:The wallet is archived.`,
  'transfer.received_amount_required': () =>
    $localize`:@@error.receivedAmountRequired:The wallets use different currencies: enter the amount received.`,
  'transfer.received_amount_mismatch': () =>
    $localize`:@@error.receivedAmountMismatch:Both wallets use the same currency: the amount received must be the same.`,
  'contact.user_not_found': () =>
    $localize`:@@error.userNotFound:No registered user has that email. They must log in to Ledger once first.`,
  'contact.in_use': () =>
    $localize`:@@error.contactInUse:This contact already has debts or groups. Add a new contact with the user's email instead.`,
  'contact.self': () => $localize`:@@error.contactSelf:You cannot add yourself as a contact.`,
  'split.no_debts': () =>
    $localize`:@@error.splitNoDebts:Nobody owes anything in this split. Record it as a regular expense instead.`,
  'split.percent_total': () => $localize`:@@error.percentTotal:Percentages must add up to 100.`,
  'split.amount_total': () => $localize`:@@error.amountTotal:The shares must add up to the total.`,
  'split.payers_total': () => $localize`:@@error.payersTotal:What the payers paid must add up to the total.`,
  'split.unknown_person': () =>
    $localize`:@@error.unknownPerson:One of the people is not available yet. Try again in a moment.`,
  'settlement.not_suggested': () =>
    $localize`:@@error.notSuggested:That payment is no longer suggested. Refresh the group balances.`,
  'adjustment.no_difference': () =>
    $localize`:@@error.noDifference:The wallet already has that balance: there is nothing to adjust.`,
  'group.member_has_activity': () =>
    $localize`:@@error.memberHasActivity:This member already took part in group expenses and cannot be removed.`,
};

export function errorMessage(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return $localize`:@@error.unexpected:Something went wrong. Please try again.`;
  }

  if (error.status === 0) {
    return $localize`:@@error.apiUnreachable:The Ledger API is not reachable. Is it running?`;
  }

  const problem = (error.error ?? {}) as ProblemDetails;

  if (problem.code && byCode[problem.code]) {
    return byCode[problem.code]();
  }

  if (problem.errors) {
    return Object.values(problem.errors).flat().join(' ');
  }

  switch (error.status) {
    case 401:
      return $localize`:@@error.unauthorized:Your session expired. Please log in again.`;
    case 404:
      return $localize`:@@error.notFound:It was not found. If you just created it, try again in a moment.`;
    case 409:
      return problem.detail ?? $localize`:@@error.conflict:It conflicts with existing data.`;
    case 503:
      return $localize`:@@error.notYetAvailable:Still being set up. Try again in a moment.`;
  }

  return problem.detail ?? problem.title ?? $localize`:@@error.unexpected:Something went wrong. Please try again.`;
}
