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
  'investment.trade_pending': () =>
    $localize`:@@error.tradePending:Another trade of this asset is still being recorded. Try again in a moment.`,
  'investment.backdated': () =>
    $localize`:@@error.tradeBackdated:Trades can't be dated before the latest trade of the asset.`,
  'investment.insufficient_quantity': () => $localize`:@@error.insufficientQuantity:You don't hold that much of this asset.`,
  'investment.total_zero': () =>
    $localize`:@@error.tradeTotalZero:Quantity × price is less than one cent: it can't be recorded.`,
  'investment.not_empty': () => $localize`:@@error.assetNotEmpty:Only assets you no longer hold can be archived.`,
  'investment.archived': () => $localize`:@@error.assetArchived:The asset is archived.`,
  'investment.quantity_too_many_decimals': () =>
    $localize`:@@error.quantityDecimals:The quantity can have at most 12 decimals.`,
  'investment.price_too_many_decimals': () => $localize`:@@error.priceDecimals:The price can have at most 8 decimals.`,
  'contact.link_pending': () =>
    $localize`:@@error.linkPending:This contact is being linked to their Ledger account. Try again in a moment.`,
  'contact.merged': () => $localize`:@@error.contactMerged:This contact was merged into another one.`,
  'link.answered': () => $localize`:@@error.linkAnswered:This link request was already answered.`,
  'books.locked': () =>
    $localize`:@@error.booksLocked:Your books are closed for that date. Move the lock date back in Settings to change it.`,
  'books.locked_for_other': () =>
    $localize`:@@error.booksLockedForOther:Someone involved closed their books for that date, so it cannot change.`,
  'debt.settled': () =>
    $localize`:@@error.debtPaid:It was already paid (fully or partly). Change or delete those payments first, then change it.`,
  'payment.pending': () =>
    $localize`:@@error.paymentPending:Another payment with this person is being recorded or changed. Try again once it is done.`,
  'payment.nothing': () => $localize`:@@error.paymentNothing:There is nothing to pay: enter an amount.`,
  'payment.amount_positive': () => $localize`:@@error.paymentAmount:The amount must be greater than zero.`,
  'payment.self': () => $localize`:@@error.paymentSelf:A person cannot pay themselves.`,
  'payment.too_much': () =>
    $localize`:@@error.paymentTooMuch:The debts changed meanwhile. Check them and record the payment again.`,
  'payment.incomplete': () => $localize`:@@error.paymentIncomplete:Choose the person, the currency and who paid.`,
  'operation.change_pending': () =>
    $localize`:@@error.changePending:A change is waiting for confirmation. Try again once it is answered.`,
  'operation.not_current': () => $localize`:@@error.notCurrent:Only the current version can be changed.`,
  'operation.not_latest': () => $localize`:@@error.notLatest:Only the latest version can be restored.`,
  'operation.not_restorable': () =>
    $localize`:@@error.settlementNotRestorable:A deleted settlement cannot be restored: settle the debts again instead.`,
  'operation.initiator_only': () =>
    $localize`:@@error.initiatorOnly:Only the person who recorded it can change the currency.`,
  'transaction.not_current': () => $localize`:@@error.notCurrent:Only the current version can be changed.`,
  'transaction.not_editable': () =>
    $localize`:@@error.notEditable:This transaction cannot be edited here. Loans, splits and settlements are changed in Debts & splits.`,
  'transaction.not_deletable': () =>
    $localize`:@@error.notDeletable:This transaction cannot be deleted here. Loans, splits and settlements are changed in Debts & splits.`,
  'change.already_answered': () => $localize`:@@error.alreadyAnswered:You already answered this change.`,
  'change.not_pending': () => $localize`:@@error.changeNotPending:This change is no longer waiting for an answer.`,
  'settlement.wallet_required': () =>
    $localize`:@@error.settlementWallet:Choose the wallet the money comes from or goes to.`,
};

export function errorMessage(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return $localize`:@@error.unexpected:Something went wrong. Please try again.`;
  }

  if (error.status === 0) {
    return typeof navigator !== 'undefined' && navigator.onLine === false
      ? $localize`:@@error.offline:You are offline. Try again when you have a connection.`
      : $localize`:@@error.apiUnreachable:The Ledger API is not reachable. Is it running?`;
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
