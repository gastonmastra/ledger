import { Browser, Page, expect, test } from '@playwright/test';
import { login, unique } from './helpers';

async function asBob(browser: Browser): Promise<Page> {
  const page = await (await browser.newContext({ locale: 'en-US' })).newPage();
  await login(page, 'bob');
  return page;
}

test.describe('Ledger web: linking a contact that already has debts', () => {
  test('alice sends a link request, bob previews and accepts it', async ({ page, browser }) => {
    const bob = await asBob(browser);
    await login(page);
    const contactName = unique('E2E Pedro');
    const dialog = page.getByRole('dialog');

    // A contact without an account, with a debt from before using Ledger.
    await page.goto('contacts');
    await page.getByRole('button', { name: 'New contact' }).click();
    await dialog.getByLabel('Name').fill(contactName);
    await dialog.getByRole('button', { name: 'Create' }).click();
    await expect(page.getByRole('row', { name: new RegExp(contactName) })).toBeVisible();

    await page.goto('debts');
    await page.getByRole('button', { name: 'Add existing debt' }).click();
    await dialog.getByRole('radio', { name: 'They owed me' }).click();
    await dialog.getByLabel('Contact').selectOption({ label: contactName });
    await dialog.getByLabel('Amount').fill('25');
    await dialog.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText('Existing debt recorded.')).toBeVisible({ timeout: 30_000 });

    // The person turns out to be Bob: linking needs his approval.
    await page.goto('contacts');
    const row = page.getByRole('row', { name: new RegExp(contactName) });
    await row.getByRole('button', { name: 'Link' }).click();
    await expect(dialog).toContainText('They will be asked to accept');
    await dialog.getByLabel('Email of the user').fill('bob@ledger.local');
    await dialog.getByRole('button', { name: 'Send request' }).click();
    await expect(page.getByText(/Request sent/)).toBeVisible();
    await expect(row).toContainText('Waiting for them to accept');

    // Bob sees what accepting puts in his books, and accepts.
    await bob.goto('changes');
    const card = bob.locator('app-incoming-link-request', { hasText: contactName });
    await expect(card).toContainText('you owe €25.00', { timeout: 30_000 });
    await card.getByRole('button', { name: 'Accept' }).click();
    await expect(bob.getByText('Linked. The shared history is now in your books.')).toBeVisible({ timeout: 30_000 });

    // Alice's contact is now Bob's account (or folded into the contact she already had for him).
    await expect
      .poll(async () => {
        await page.reload();
        const rows = page.getByRole('row', { name: new RegExp(contactName) });
        return (await rows.count()) === 0 || (await rows.first().innerText()).includes('Linked');
      }, { timeout: 30_000 })
      .toBe(true);

    await bob.close();
  });
});
