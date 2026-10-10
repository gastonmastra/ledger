import { expect, test } from '@playwright/test';
import { login } from './helpers';

test.describe('Ledger web: hide amounts', () => {
  test('masks money in the dashboard, remembers it and toggles with Shift+H', async ({ page }) => {
    await login(page);
    const net = page.locator('ui-stat').first();
    await expect(net).toContainText(/\d/);

    await page.getByRole('button', { name: 'Hide amounts (Shift+H)' }).click();
    await expect(net).toContainText('•••••');
    await expect(net).not.toContainText(/\d/);

    // Remembered after reloading.
    await page.reload();
    await expect(page.getByRole('button', { name: 'Show amounts (Shift+H)' })).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('ui-stat').first()).toContainText('•••••');

    await page.locator('body').press('Shift+H');
    await expect(page.locator('ui-stat').first()).toContainText(/\d/);
    await expect(page.getByRole('button', { name: 'Hide amounts (Shift+H)' })).toBeVisible();
  });
});
