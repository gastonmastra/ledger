import { Browser, Page, expect, test } from '@playwright/test';
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

async function recordExpense(page: Page, walletName: string, amount: string, description: string): Promise<void> {
  const dialog = page.getByRole('dialog');
  await page.goto('transactions');
  await page.getByRole('button', { name: 'Expense' }).click();
  await selectOptionContaining(dialog.getByLabel('Wallet'), walletName);
  await dialog.getByLabel(/Amount/).fill(amount);
  await dialog.getByLabel('Description').fill(description);
  await dialog.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Expense recorded.')).toBeVisible();
}

/** Bob has to log in once so that Alice can link him as a contact. */
async function asBob(browser: Browser): Promise<Page> {
  const page = await (await browser.newContext({ locale: 'en-US' })).newPage();
  await login(page, 'bob');
  return page;
}

test.describe('Ledger web: editable, auditable ledger', () => {
  test('edits, deletes and restores an expense, keeping the history and the totals right', async ({ page }) => {
    await login(page);
    const walletName = unique('E2E edits');
    const description = unique('E2E lunch');
    const dialog = page.getByRole('dialog');
    await createWallet(page, walletName, '100');
    await recordExpense(page, walletName, '12', description);

    await filterTransactions(page, { wallet: walletName });
    await expect(page.getByText(`Wallet: ${walletName}`)).toBeVisible(); // the active filter as a chip
    const item = page.locator('li', { hasText: description }).first();

    // Clicking the row opens its detail, with the same actions as its ⋯ menu.
    await item.click();
    await expect(dialog.getByText('Movements')).toBeVisible();
    await dialog.getByRole('button', { name: 'Edit' }).click();
    await expect(dialog.getByLabel(/Amount/)).toHaveValue('12');
    await dialog.getByLabel(/Amount/).fill('20');
    await expect(dialog.getByRole('button', { name: 'Save' })).toBeDisabled(); // the reason is required
    await dialog.getByLabel('Why?').fill('E2E: it was 20');
    await dialog.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Corrected. The previous version is kept in the history.')).toBeVisible();

    // Only the current version is listed (and counted).
    await expect(page.locator('li', { hasText: description })).toHaveCount(1);
    await expect(page.locator('li', { hasText: description }).first()).toContainText('Edited');
    await expect(page.locator('li', { hasText: description }).first()).toContainText('-€20.00');

    await rowAction(page.locator('li', { hasText: description }).first(), 'History');
    // The reason is on the reversal and on the corrected version.
    await expect(dialog.getByText('E2E: it was 20')).toHaveCount(2);
    await expect(dialog.locator('li', { hasText: 'Changed' })).toHaveCount(1);
    await dialog.getByRole('button', { name: 'Close' }).last().click();

    // "Show changes" also lists the replaced version and its reversal.
    await filterTransactions(page, { showChanges: true });
    await expect(page.locator('li', { hasText: description })).toHaveCount(3);
    await filterTransactions(page, { showChanges: false });

    await rowAction(page.locator('li', { hasText: description }).first(), 'Delete');
    await dialog.getByLabel('Why?').fill('E2E: duplicated');
    await dialog.getByRole('button', { name: 'Delete' }).click();
    await expect(page.getByText('Transaction deleted.')).toBeVisible();
    await expect(page.locator('li', { hasText: description })).toHaveCount(0);

    await filterTransactions(page, { showChanges: true });
    await rowAction(page.locator('li', { hasText: description }).filter({ hasText: 'Deleted' }).first(), 'History');
    await dialog.getByRole('button', { name: 'Restore' }).click();
    await dialog.getByLabel('Why?').fill('E2E: it was not duplicated');
    await dialog.getByRole('button', { name: 'Restore' }).click();
    await expect(page.getByText('Transaction restored.')).toBeVisible();

    await page.goto('activity');
    // This run's entries (earlier runs leave the same reasons on other expenses).
    await expect(page.getByText(description).filter({ hasText: '“E2E: it was not duplicated”' })).toBeVisible();
    await expect(page.getByText(description).filter({ hasText: '“E2E: duplicated”' })).toBeVisible();
  });

  test('a lock date blocks changes in the closed period', async ({ page }) => {
    await login(page);
    const walletName = unique('E2E lock');
    const dialog = page.getByRole('dialog');
    await createWallet(page, walletName, '50');

    await page.goto('settings');
    await page.getByLabel('Closed up to').fill(localInput(new Date()));
    await page.getByRole('button', { name: 'Lock', exact: true }).click();
    await expect(page.getByText('Books closed.')).toBeVisible();

    try {
      await page.goto('transactions');
      await page.getByRole('button', { name: 'Expense' }).click();
      await selectOptionContaining(dialog.getByLabel('Wallet'), walletName);
      await dialog.getByLabel(/Amount/).fill('5');
      await dialog.getByLabel('When').fill(localInput(new Date(Date.now() - 86_400_000)));
      await dialog.getByRole('button', { name: 'Save' }).click();
      await expect(page.getByText(/Your books are closed for that date/)).toBeVisible();
      await dialog.getByRole('button', { name: 'Cancel' }).click();
    } finally {
      await page.goto('settings');
      await page.getByRole('button', { name: 'Unlock everything' }).click();
      await expect(page.getByText('Everything is open again.')).toBeVisible();
    }
  });

  test('a linked user confirms a change of a shared loan', async ({ page, browser }) => {
    const bob = await asBob(browser);
    await login(page);
    const walletName = unique('E2E loan');
    const description = unique('E2E loan to Bob');
    const dialog = page.getByRole('dialog');
    await createWallet(page, walletName, '100');

    // Link Bob once (by email); later runs reuse the contact.
    await page.goto('contacts');
    await page.locator('ui-table, ui-empty-state').first().waitFor(); // the list is loaded before looking
    const existing = page.getByRole('row', { name: /bob@ledger\.local/ });
    let bobName = 'E2E Bob';
    if (await existing.count()) {
      bobName = (await existing.first().locator('td').first().innerText()).trim();
    } else {
      await page.getByRole('button', { name: 'New contact' }).click();
      await dialog.getByLabel('Name').fill(bobName);
      await dialog.getByLabel(/Email/).fill('bob@ledger.local');
      await dialog.getByRole('button', { name: 'Create' }).click();
      await expect(page.getByRole('row', { name: new RegExp(bobName) })).toBeVisible();
    }

    await page.goto('debts');
    await menuAction(page, 'Add', 'Lend money');
    await dialog.getByLabel('To').selectOption({ label: bobName });
    await selectOptionContaining(dialog.getByLabel('From wallet'), walletName);
    await dialog.getByLabel('Amount').fill('10');
    await dialog.getByLabel('Description').fill(description);
    await dialog.getByRole('button', { name: 'Lend' }).click();
    await expect(page.getByText(`Loan to ${bobName} recorded.`)).toBeVisible({ timeout: 30_000 });

    const operation = page.locator('li', { hasText: description }).first();
    await rowAction(operation, 'Edit');
    await dialog.getByLabel('Amount').fill('15');
    await dialog.getByLabel('Why?').fill('E2E: I lent him 15');
    await dialog.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText(/Sent for confirmation to/)).toBeVisible();
    await expect(page.locator('li', { hasText: description }).first()).toContainText('Change pending');

    await bob.goto('changes');
    const change = bob.locator('ui-card', { hasText: 'E2E: I lent him 15' });
    await expect(change).toContainText('€10.00');
    await expect(change).toContainText('€15.00');
    await change.getByRole('button', { name: 'Accept' }).click();
    await expect(bob.getByText('Thanks, your answer was recorded.')).toBeVisible({ timeout: 30_000 });

    await page.reload();
    const updated = page.locator('li', { hasText: description }).first();
    await expect(updated).toContainText('€15.00');
    await expect(updated).not.toContainText('Change pending');
    await bob.context().close();
  });
});

/** Value for a datetime-local input. */
function localInput(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
