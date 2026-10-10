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
