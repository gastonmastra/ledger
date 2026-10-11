import { Page, expect, test } from '@playwright/test';
import { filterTransactions, login, menuAction, rowAction, selectOptionContaining, unique } from './helpers';

async function createWallet(page: Page, name: string, opening: string): Promise<void> {
  const dialog = page.getByRole('dialog');
  await page.goto('wallets');
  await page.getByRole('button', { name: 'New wallet' }).click();
  await dialog.getByLabel('Name').fill(name);
  await dialog.getByLabel('Opening balance').fill(opening);
  await dialog.getByRole('button', { name: 'Create' }).click();
  await expect(page.getByRole('row', { name: new RegExp(name) })).toContainText('Active');
}

test.describe('Ledger web: dates, balances and existing debts', () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test('records an expense on a chosen date', async ({ page }) => {
    const walletName = unique('E2E dated');
    await createWallet(page, walletName, '100');

    await page.goto('transactions');
    await page.getByRole('button', { name: 'Expense' }).click();
    const dialog = page.getByRole('dialog');
    await selectOptionContaining(dialog.getByLabel('Wallet'), walletName);
    await dialog.getByLabel(/Amount/).fill('5');
    await dialog.getByLabel('Description').fill('E2E coffee in January');
    await dialog.getByLabel('When').fill('2026-01-15T10:30');
    await dialog.getByRole('button', { name: 'Save' }).click();

    await expect(page.getByText('Expense recorded.')).toBeVisible();
    // Newest first: an expense dated in January is further down, so filter by its wallet.
    await filterTransactions(page, { wallet: walletName });
    const item = page.locator('li', { hasText: 'E2E coffee in January' }).first();
    await expect(item).toContainText('Jan 15, 2026');
  });

  test('changes the opening balance and adjusts a wallet to its real balance', async ({ page }) => {
    const walletName = unique('E2E opening');
    await createWallet(page, walletName, '100');
    const row = page.getByRole('row', { name: new RegExp(walletName) });
    const dialog = page.getByRole('dialog');

    await rowAction(row, 'Opening balance');
    await expect(dialog.getByLabel(/Amount/)).toHaveValue('100');
    await dialog.getByLabel(/Amount/).fill('250');
    await dialog.getByLabel('Why?').fill('E2E: counted wrong');
    await dialog.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Opening balance updated.')).toBeVisible();
    await expect(row).toContainText('250.00');

    // Also from the wallet's detail (click the row).
    await row.click();
    await expect(dialog.getByRole('heading', { name: walletName })).toBeVisible();
    await dialog.getByRole('button', { name: 'Adjust balance' }).click();
    await dialog.getByLabel('Real balance').fill('240');
    await dialog.getByLabel('Description').fill('E2E bank fees');
    await dialog.getByRole('button', { name: 'Adjust' }).click();
    await expect(page.getByText('Balance adjusted.')).toBeVisible();
    await expect(row).toContainText('240.00');
  });

  test('adds an existing debt and pays it', async ({ page }) => {
    const walletName = unique('E2E payback');
    const contactName = unique('E2E Sofi');
    const dialog = page.getByRole('dialog');
    await createWallet(page, walletName, '100');

    await page.goto('contacts');
    await page.getByRole('button', { name: 'New contact' }).click();
    await dialog.getByLabel('Name').fill(contactName);
    await dialog.getByRole('button', { name: 'Create' }).click();
    await expect(page.getByRole('row', { name: new RegExp(contactName) })).toBeVisible();

    await page.goto('debts');
    await menuAction(page, 'Add', 'Add existing debt');
    await dialog.getByRole('radio', { name: 'I owed them' }).click();
    await dialog.getByLabel('Contact').selectOption({ label: contactName });
    await dialog.getByLabel('Amount').fill('30');
    await dialog.getByRole('button', { name: 'Save' }).click();

    await expect(page.getByText('Existing debt recorded.')).toBeVisible({ timeout: 30_000 });
    const debtRow = page.getByRole('row', { name: new RegExp(contactName) }).last();
    await expect(debtRow).toContainText('you owe €30.00');

    // Pre-filled to pay everything I owe them.
    await page.getByRole('row', { name: new RegExp(contactName) }).first().getByRole('button', { name: 'Record payment' }).click();
    await expect(dialog.getByLabel('Amount')).toHaveValue('30');
    await expect(dialog.getByText('Paid', { exact: true })).toBeVisible();
    await selectOptionContaining(dialog.getByLabel('From wallet'), walletName);
    await dialog.getByRole('button', { name: 'Record payment' }).click();
    await expect(page.getByText('Payment recorded.')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole('row', { name: new RegExp(contactName) })).toHaveCount(0);
  });

  test('a payment pays the oldest debt first, then is changed and undone', async ({ page }) => {
    const walletName = unique('E2E brother wallet');
    const contactName = unique('E2E Brother');
    const dialog = page.getByRole('dialog');
    await createWallet(page, walletName, '10');

    await page.goto('contacts');
    await page.getByRole('button', { name: 'New contact' }).click();
    await dialog.getByLabel('Name').fill(contactName);
    await dialog.getByRole('button', { name: 'Create' }).click();
    await expect(page.getByRole('row', { name: new RegExp(contactName) })).toBeVisible();

    // He owes me 150 (older) and 50 (newer).
    await page.goto('debts');
    for (const amount of ['150', '50']) {
      await menuAction(page, 'Add', 'Add existing debt');
      await dialog.getByRole('radio', { name: 'They owed me' }).click();
      await dialog.getByLabel('Contact').selectOption({ label: contactName });
      await dialog.getByLabel('Amount').fill(amount);
      await dialog.getByRole('button', { name: 'Save' }).click();
      await expect(page.getByText('Existing debt recorded.').last()).toBeVisible({ timeout: 30_000 });
    }
    const byPerson = page.getByRole('row', { name: new RegExp(contactName) }).first();
    await expect(byPerson).toContainText('€200.00');

    // He pays 170: the 150 is paid, 30 are left of the 50.
    await byPerson.getByRole('button', { name: 'Record payment' }).click();
    await dialog.getByLabel('Amount').fill('170');
    await expect(dialog.getByText('€30.00 left of €50.00')).toBeVisible();
    await selectOptionContaining(dialog.getByLabel('Into wallet'), walletName);
    await dialog.getByRole('button', { name: 'Record payment' }).click();
    await expect(page.getByText('Payment recorded.')).toBeVisible({ timeout: 30_000 });

    const debtRows = page.getByRole('row', { name: new RegExp(contactName) });
    await expect(debtRows).toHaveCount(2); // the person's balance and the partially paid debt
    await expect(debtRows.last()).toContainText('owes you €30.00');
    await expect(debtRows.last()).toContainText('of €50.00');
    await expect(debtRows.last()).toContainText('Partially paid');

    // He actually paid 130: 20 left of the 150, the 50 untouched.
    const payment = page.getByRole('listitem').filter({ hasText: `${contactName} paid` }).first();
    await rowAction(payment, 'Edit');
    await dialog.getByLabel('Amount').fill('130');
    await expect(dialog.getByText('€20.00 left of €150.00')).toBeVisible();
    await dialog.getByLabel('Why?').fill('E2E: it was 130');
    await dialog.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Change applied.')).toBeVisible({ timeout: 30_000 });
    await expect(debtRows).toHaveCount(3);
    await expect(debtRows.filter({ hasText: 'owes you €20.00' })).toContainText('of €150.00');
    await expect(debtRows.filter({ hasText: 'owes you €50.00' })).not.toContainText('Partially paid');

    // Undone: everything is owed again.
    // From the payment's detail (click it): what it paid, and its actions.
    await page.getByRole('listitem').filter({ hasText: `${contactName} paid` }).first().click();
    await expect(dialog.getByText('What it pays')).toBeVisible();
    await dialog.getByRole('button', { name: 'Delete' }).click();
    await dialog.getByLabel('Why?').fill('E2E: he did not pay');
    await dialog.getByRole('button', { name: 'Delete' }).click();
    await expect(page.getByText('Deleted.')).toBeVisible({ timeout: 30_000 });
    await expect(byPerson).toContainText('€200.00');
  });
});

test.describe('Ledger web: dashboard charts', () => {
  test('shows charts for the month, the year and a custom range', async ({ page }) => {
    await login(page);
    await createWallet(page, unique('E2E charts'), '75');
    await page.goto('./');

    const now = new Date();
    const monthTitle = now.toLocaleString('en-US', { month: 'long', year: 'numeric' });
    await expect(page.getByRole('heading', { name: monthTitle })).toBeVisible();
    await expect(page.getByRole('img', { name: 'Net worth over time' })).toBeVisible();
    await expect(page.getByRole('img', { name: 'Balance per wallet' })).toBeVisible();

    await page.getByRole('radio', { name: 'Year' }).click();
    await expect(page.getByRole('heading', { name: String(now.getFullYear()), exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Previous period' }).click();
    await expect(page.getByRole('heading', { name: String(now.getFullYear() - 1), exact: true })).toBeVisible();

    await page.getByRole('radio', { name: 'Custom' }).click();
    await expect(page.getByLabel('From')).toBeVisible();
    await expect(page.getByLabel('Currency')).toBeVisible();
  });
});
