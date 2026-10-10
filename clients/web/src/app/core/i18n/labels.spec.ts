import { accountLabel, debtStatusLabel, transactionKindLabel } from './labels';

describe('labels', () => {
  it('shows system accounts in the user language instead of the API name', () => {
    expect(accountLabel({ accountName: 'Owed to me', accountPurpose: 'Receivables' })).toBe('Owed to me');
    expect(accountLabel({ accountName: 'Pending settlements', accountPurpose: 'PendingSettlements' })).toBe(
      'Pending settlements',
    );
  });

  it('keeps the names the user chose for wallets and categories', () => {
    expect(accountLabel({ accountName: 'My bank', accountPurpose: 'Wallet' })).toBe('My bank');
    expect(accountLabel({ accountName: 'Food', accountPurpose: 'Category' })).toBe('Food');
  });

  it('labels kinds and statuses', () => {
    expect(transactionKindLabel('WalletAssignment')).toBe('Wallet assigned');
    expect(debtStatusLabel('Open', true)).toBe('Settling');
    expect(debtStatusLabel('Settled', false)).toBe('Settled');
  });
});
