import { expect, test } from '@playwright/test';
import { filterTransactions, login, rowAction, unique } from './helpers';

/** The production build (container) has the service worker; `ng serve` does not. */
const production = !!process.env['E2E_BASE_URL'];

test.describe('Ledger web: grouped actions and details', () => {
  test.use({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });

  test('on a phone: filters in a bottom sheet, ⋯ menus and detail pop-ups', async ({ page }) => {
    await login(page);
    const dialog = page.getByRole('dialog');
    const walletName = unique('E2E phone');

    await page.goto('wallets');
    await page.getByRole('button', { name: 'New wallet' }).click();
    await dialog.getByLabel('Name').fill(walletName);
    await dialog.getByLabel('Opening balance').fill('42');
    await dialog.getByRole('button', { name: 'Create' }).click();
    const row = page.getByRole('row', { name: new RegExp(walletName) });
    await expect(row).toContainText('Active');

    // The row's actions are grouped in its ⋯ menu.
    await row.getByRole('button', { name: 'More actions' }).click();
    for (const action of ['Opening balance', 'Adjust balance', 'Rename', 'Archive', 'Delete']) {
      await expect(page.getByRole('menuitem', { name: action, exact: true })).toBeVisible();
    }
    await page.keyboard.press('Escape');

    // Clicking the row opens its detail.
    await row.click();
    await expect(dialog.getByRole('heading', { name: walletName })).toBeVisible();
    await expect(dialog).toContainText('€42.00');
    await dialog.getByRole('button', { name: 'Close' }).first().click();

    // Filters live in a bottom sheet on phones; active ones show as chips.
    await page.goto('transactions');
    await expect(page.getByLabel('Category')).toHaveCount(0); // not on the page any more
    await page.getByRole('button', { name: /^Filters/ }).click();
    const box = (await dialog.boundingBox())!;
    expect(Math.round(box.y + box.height)).toBeGreaterThanOrEqual(840); // anchored to the bottom
    await page.keyboard.press('Escape');

    await filterTransactions(page, { wallet: walletName });
    const chip = page.getByText(`Wallet: ${walletName}`);
    await expect(chip).toBeVisible();
    await expect(page.getByRole('button', { name: /^Filters/ })).toContainText('1');
    const opening = page.getByRole('listitem').filter({ hasText: 'Opening balance' }).first();
    await expect(opening).toBeVisible();
    await rowAction(opening, 'History');
    await expect(dialog.getByRole('heading', { name: 'History' })).toBeVisible();
    await dialog.getByRole('button', { name: 'Close' }).last().click();

    await page.getByRole('button', { name: 'Remove filter' }).click();
    await expect(chip).toHaveCount(0);
  });
});

test.describe('Ledger web: installable app', () => {
  test.skip(!production, 'needs the production build (E2E_BASE_URL), which has the service worker');

  test('has a manifest and a service worker, and opens offline', async ({ page, context, request, baseURL }) => {
    const manifest = await request.get(new URL('manifest.webmanifest', baseURL).toString());
    expect(manifest.headers()['content-type']).toContain('application/manifest+json');
    expect(manifest.headers()['cache-control']).toContain('no-cache');
    expect((await manifest.json()).display).toBe('standalone');

    const ngsw = await request.get(new URL('ngsw.json', baseURL).toString());
    expect(ngsw.headers()['cache-control']).toContain('no-cache');

    await login(page);
    const scope = await page.evaluate(async () => (await navigator.serviceWorker.ready).scope);
    expect(scope).toMatch(/\/(en|es)\/$/);
    // Wait until the worker controls the page and has cached the app.
    await page.reload();
    await expect.poll(() => page.evaluate(() => !!navigator.serviceWorker.controller), { timeout: 30_000 }).toBe(true);

    // Offline: the app still opens (from the cache) and says so.
    await context.setOffline(true);
    await page.reload();
    await expect(page.getByText('You are offline.', { exact: false })).toBeVisible({ timeout: 30_000 });
    // The app shell (navigation) is there; the data needs the connection.
    await expect(page.getByRole('link', { name: 'Wallets' }).first()).toBeVisible();
    await context.setOffline(false);
  });
});
