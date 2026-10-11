import { Page, expect, test } from '@playwright/test';
import { login, rowAction, selectOptionContaining, unique } from './helpers';

async function createWallet(page: Page, name: string, opening: string): Promise<void> {
  const dialog = page.getByRole('dialog');
  await page.goto('wallets');
  await page.getByRole('button', { name: 'New wallet' }).click();
  await dialog.getByLabel('Name').fill(name);
  await dialog.getByLabel('Currency').selectOption('USD');
  await dialog.getByLabel('Opening balance').fill(opening);
  await dialog.getByRole('button', { name: 'Create' }).click();
  await expect(page.getByRole('row', { name: new RegExp(name) })).toContainText('Active');
}

test.describe('Ledger web: investments', () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test('buys a new asset, updates its price, sells part of it and shows it in the dashboard', async ({ page }) => {
    const walletName = unique('E2E broker');
    const assetName = unique('E2E Coin');
    await createWallet(page, walletName, '1000');

    await page.goto('investments');
    await page.getByRole('button', { name: 'Buy' }).first().click();
    const dialog = page.getByRole('dialog');

    // A new asset, added while buying.
    await dialog.getByLabel('Asset').selectOption('__new');
    await dialog.getByLabel('Name').fill(assetName);
    await dialog.getByLabel('Symbol').fill('E2E');
    await dialog.getByLabel('Type').selectOption('Crypto');
    await dialog.getByLabel('Quoted in').selectOption('USD');
    await dialog.getByLabel('Quantity').fill('2');
    await dialog.getByLabel('Price per unit paid').fill('100');
    await expect(dialog).toContainText('$200.00');
    await selectOptionContaining(dialog.getByLabel('From wallet'), walletName);
    await dialog.getByLabel('Current sell price').fill('95');
    await dialog.getByRole('button', { name: 'Buy', exact: true }).click();

    await expect(page.getByText('Purchase recorded.')).toBeVisible();
    const row = page.getByRole('row', { name: new RegExp(assetName) });
    await expect(row).toContainText('$190.00'); // 2 × sell price 95
    await expect(row).toContainText('-$10.00');

    // A new sell price changes the value; no money moves.
    await rowAction(row, 'Update price');
    await dialog.getByLabel('Sell price').fill('150');
    await dialog.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Price recorded.')).toBeVisible();
    await expect(row).toContainText('$300.00');

    await rowAction(row, 'Sell');
    await dialog.getByLabel('Quantity').fill('1');
    await dialog.getByLabel('Price per unit received').fill('150');
    await selectOptionContaining(dialog.getByLabel('Into wallet'), walletName);
    await dialog.getByRole('button', { name: 'Sell', exact: true }).click();

    await expect(page.getByText('Sale recorded.')).toBeVisible();
    await expect(row).toContainText('$150.00');
    // The sale in the trades list, with its realized gain (150 received − 100 average cost).
    await expect(page.locator('app-trades-table').getByRole('row').filter({ hasText: '+$50.00' }).first()).toContainText('Sell');

    await page.goto('wallets');
    await expect(page.getByRole('row', { name: new RegExp(walletName) })).toContainText('$950.00');

    await page.goto('./');
    await expect(page.getByText('Money (net)').first()).toBeVisible();
    await page.getByLabel('Currency').selectOption('USD');
    await expect(page.getByRole('heading', { name: 'Investments over time' })).toBeVisible();
    await expect(page.getByRole('row', { name: new RegExp(assetName) })).toContainText('$150.00');
  });
});
