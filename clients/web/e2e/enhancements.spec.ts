import { Page, expect, test } from '@playwright/test';
import { login, selectOptionContaining, unique } from './helpers';

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
    const item = page.locator('li', { hasText: 'E2E coffee in January' }).first();
    await expect(item).toContainText('Jan 15, 2026');
  });

  test('changes the opening balance and adjusts a wallet to its real balance', async ({ page }) => {
    const walletName = unique('E2E opening');
    await createWallet(page, walletName, '100');
    const row = page.getByRole('row', { name: new RegExp(walletName) });
    const dialog = page.getByRole('dialog');

    await row.getByRole('button', { name: 'Opening balance' }).click();
    await expect(dialog.getByLabel(/Amount/)).toHaveValue('100');
    await dialog.getByLabel(/Amount/).fill('250');
    await dialog.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Opening balance updated.')).toBeVisible();
    await expect(row).toContainText('250.00');

    await row.getByRole('button', { name: 'Adjust balance' }).click();
    await dialog.getByLabel('Real balance').fill('240');
    await dialog.getByLabel('Description').fill('E2E bank fees');
    await dialog.getByRole('button', { name: 'Adjust' }).click();
    await expect(page.getByText('Balance adjusted.')).toBeVisible();
    await expect(row).toContainText('240.00');
  });

  test('adds an existing debt and settles it', async ({ page }) => {
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
    await page.getByRole('button', { name: 'Add existing debt' }).click();
    await dialog.getByRole('radio', { name: 'I owed them' }).click();
    await dialog.getByLabel('Contact').selectOption({ label: contactName });
    await dialog.getByLabel('Amount').fill('30');
    await dialog.getByRole('button', { name: 'Save' }).click();

    await expect(page.getByText('Existing debt recorded.')).toBeVisible({ timeout: 30_000 });
    const debtRow = page.getByRole('row', { name: new RegExp(contactName) }).last();
    await expect(debtRow).toContainText('you owe €30.00');

    await debtRow.getByRole('button', { name: 'Settle', exact: true }).click();
    await selectOptionContaining(dialog.getByLabel('Wallet'), walletName);
    await dialog.getByRole('button', { name: 'Settle' }).click();
    await expect(page.getByText(`Debt with ${contactName} settled.`)).toBeVisible({ timeout: 30_000 });
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
