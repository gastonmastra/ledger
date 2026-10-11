import { expect, test } from '@playwright/test';
import { login, menuAction, selectOptionContaining, unique } from './helpers';

test.describe('Ledger web', () => {
  test.beforeEach(async ({ page }) => {
    await login(page);
  });

  test('creates a wallet and records an expense', async ({ page }) => {
    const walletName = unique('E2E wallet');

    await page.getByRole('link', { name: 'Wallets' }).first().click();
    await page.getByRole('button', { name: 'New wallet' }).click();

    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Name').fill(walletName);
    await dialog.getByLabel('Opening balance').fill('100');
    await dialog.getByRole('button', { name: 'Create' }).click();

    const row = page.getByRole('row', { name: new RegExp(walletName) });
    await expect(row).toContainText('Active'); // the ledger finished setting it up
    await expect(row).toContainText('100.00');

    await page.getByRole('link', { name: 'Transactions' }).first().click();
    await page.getByRole('button', { name: 'Expense' }).click();
    await selectOptionContaining(dialog.getByLabel('Wallet'), walletName);
    await dialog.getByLabel(/Amount/).fill('12.5');
    await dialog.getByLabel('Description').fill('E2E lunch');
    await dialog.getByRole('button', { name: 'Save' }).click();

    await expect(page.getByText('Expense recorded.')).toBeVisible();
    await expect(page.getByText('E2E lunch').first()).toBeVisible();

    await page.getByRole('link', { name: 'Wallets' }).first().click();
    await expect(page.getByRole('row', { name: new RegExp(walletName) })).toContainText('87.50');
  });

  test('lends money to a contact and shows the debt', async ({ page }) => {
    const walletName = unique('E2E loans');
    const contactName = unique('E2E Carl');

    await page.goto('wallets');
    await page.getByRole('button', { name: 'New wallet' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Name').fill(walletName);
    await dialog.getByLabel('Opening balance').fill('50');
    await dialog.getByRole('button', { name: 'Create' }).click();
    await expect(page.getByRole('row', { name: new RegExp(walletName) })).toContainText('Active');

    await page.getByRole('link', { name: 'Contacts' }).first().click();
    await page.getByRole('button', { name: 'New contact' }).click();
    await dialog.getByLabel('Name').fill(contactName);
    await dialog.getByRole('button', { name: 'Create' }).click();
    await expect(page.getByRole('row', { name: new RegExp(contactName) })).toBeVisible();

    await page.getByRole('link', { name: 'Debts & splits' }).first().click();
    await menuAction(page, 'Add', 'Lend money');
    await dialog.getByLabel('To').selectOption({ label: contactName });
    await selectOptionContaining(dialog.getByLabel('From wallet'), walletName);
    await dialog.getByLabel('Amount').fill('20');
    await dialog.getByRole('button', { name: 'Lend' }).click();

    // Fire and notify: the dialog closes at once, a toast confirms later.
    await expect(page.getByText(`Loan to ${contactName} recorded.`)).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole('row', { name: new RegExp(contactName) }).first()).toContainText('20.00');
  });

  test('switches between light and dark theme', async ({ page }) => {
    const html = page.locator('html');
    const toggle = page.getByRole('button', { name: /Theme:/ });

    await toggle.click(); // system → light
    await expect(html).not.toHaveClass(/dark/);
    await toggle.click(); // light → dark
    await expect(html).toHaveClass(/dark/);
    await toggle.click(); // back to system
  });
});

test.describe('Ledger web: groups', () => {
  test('splits a group expense and settles the suggested payment', async ({ page }) => {
    await login(page);

    const walletName = unique('E2E trip wallet');
    const contactName = unique('E2E Dana');
    const groupName = unique('E2E trip');
    const dialog = page.getByRole('dialog');

    await page.goto('wallets');
    await page.getByRole('button', { name: 'New wallet' }).click();
    await dialog.getByLabel('Name').fill(walletName);
    await dialog.getByLabel('Opening balance').fill('200');
    await dialog.getByRole('button', { name: 'Create' }).click();
    await expect(page.getByRole('row', { name: new RegExp(walletName) })).toContainText('200.00');

    await page.goto('contacts');
    await page.getByRole('button', { name: 'New contact' }).click();
    await dialog.getByLabel('Name').fill(contactName);
    await dialog.getByRole('button', { name: 'Create' }).click();
    await expect(page.getByRole('row', { name: new RegExp(contactName) })).toBeVisible();

    await page.goto('groups');
    await page.getByRole('button', { name: 'New group' }).click();
    await dialog.getByLabel('Name').fill(groupName);
    await dialog.getByLabel(contactName).check();
    await dialog.getByRole('button', { name: 'Create' }).click();
    await expect(page.getByRole('heading', { name: groupName })).toBeVisible();

    await page.getByRole('button', { name: 'Add expense' }).click();
    await dialog.getByLabel('What was it?').fill('E2E dinner');
    await dialog.getByLabel('Total').fill('60');
    await selectOptionContaining(dialog.getByLabel('Paid from'), walletName);
    await dialog.getByRole('button', { name: 'Split', exact: true }).click();

    await expect(page.getByText('Expense split: “E2E dinner”.')).toBeVisible({ timeout: 30_000 });
    const payment = page.getByRole('row', { name: new RegExp(`${contactName} pays Me`) });
    await expect(payment).toContainText('30.00');

    await payment.getByRole('button', { name: 'Mark as paid' }).click();
    await selectOptionContaining(dialog.getByLabel('Wallet'), walletName);
    await dialog.getByRole('button', { name: 'Record payment' }).click();

    await expect(page.getByText('Payment recorded.')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText('Everyone is even.')).toBeVisible();
  });

  test('uses the bottom navigation on small screens', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await login(page);

    const bottomNav = page.locator('nav.fixed');
    await expect(bottomNav).toBeVisible();
    await bottomNav.getByRole('link', { name: 'Wallets' }).click();
    await expect(page.getByRole('heading', { name: 'Wallets' })).toBeVisible();

    await page.getByRole('button', { name: 'Open menu' }).click();
    await page.getByRole('link', { name: 'Categories' }).click();
    await expect(page.getByRole('heading', { name: 'Categories' })).toBeVisible();
  });
});

test.describe('Ledger web: languages', () => {
  test('switches between English and Spanish', async ({ page }) => {
    test.skip(!process.env['E2E_BASE_URL'], 'ng serve serves one language; run against the container (see README).');

    await login(page);
    await page.getByLabel('Language').selectOption('es');
    await expect(page.getByRole('heading', { name: 'Mi dinero' })).toBeVisible();
    await expect(page).toHaveURL(/\/es\//);

    await page.getByLabel('Idioma').selectOption('en');
    await expect(page.getByRole('heading', { name: 'My money' })).toBeVisible();
  });
});
