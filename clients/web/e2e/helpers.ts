import { Locator, Page, expect } from '@playwright/test';

/** Logs in through the Keycloak login page (test users from the imported realm). */
export async function login(page: Page, user = 'alice', password = user): Promise<void> {
  await page.goto('./');
  await page.locator('#username').fill(user);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
  await expect(page.getByRole('heading', { name: 'My money' })).toBeVisible();
}

export function unique(prefix: string): string {
  return `${prefix} ${Date.now().toString(36)}`;
}

/** Selects the option whose text contains `text` (option labels also show balances). */
export async function selectOptionContaining(select: Locator, text: string): Promise<void> {
  const option = select.locator('option', { hasText: text });
  await expect(option).toHaveCount(1);
  await select.selectOption((await option.getAttribute('value'))!);
}

/** Runs an action of a row's "⋯" menu. */
export async function rowAction(row: Locator, action: string): Promise<void> {
  await row.getByRole('button', { name: 'More actions' }).click();
  await row.page().getByRole('menuitem', { name: action, exact: true }).click();
}

/** Runs an action of a header menu ("Add ▾", "More ▾"). */
export async function menuAction(page: Page, button: string, action: string): Promise<void> {
  await page.getByRole('button', { name: button, exact: true }).click();
  await page.getByRole('menuitem', { name: action, exact: true }).click();
}

/** Opens the transactions filters panel, applies the changes and closes it. */
export async function filterTransactions(
  page: Page,
  options: { wallet?: string; showChanges?: boolean },
): Promise<void> {
  await page.getByRole('button', { name: /^Filters/ }).click();
  const panel = page.getByRole('dialog');

  if (options.wallet !== undefined) await selectOptionContaining(panel.getByLabel('Wallet'), options.wallet);
  if (options.showChanges !== undefined) await panel.getByLabel('Show changes').setChecked(options.showChanges);

  await panel.getByRole('button', { name: 'Done' }).click();
  await expect(panel).toHaveCount(0);
}
