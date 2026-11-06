import { writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from '@playwright/test';

const TOKEN = process.env['AGENT_E2E_TOKEN'] ?? 'e2e-token';
const API = process.env['AGENT_E2E_API'] ?? 'http://127.0.0.1:5055';
const WORKSPACE = process.env['AGENT_E2E_WORKSPACE'] ?? join(process.cwd(), 'e2e-workspace');

async function signIn(page: import('@playwright/test').Page): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('API token').fill(TOKEN);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/tasks$/);
}

test('signed-out visitors are sent to sign-in', async ({ page }) => {
  await page.goto('/tasks');
  await expect(page).toHaveURL(/\/login$/);
});

test('chat: a submitted task reaches Done and shows its result', async ({ page }) => {
  await signIn(page);

  const prompt = `e2e chat ${Date.now()}`;
  await page.getByLabel('What should the agent do?').fill(prompt);
  await page.getByRole('button', { name: 'Submit' }).click();

  const row = page.locator('li', { hasText: prompt }).first();
  await expect(row).toContainText('Done');
  await expect(row.locator('pre')).toContainText('offline');
});

test('diff: an exact edit shows the diff for review', async ({ page }) => {
  mkdirSync(WORKSPACE, { recursive: true });
  const file = `e2e-${Date.now()}.md`;
  writeFileSync(join(WORKSPACE, file), 'alpha\nbeta\ngamma\n');

  await signIn(page);
  await page.getByRole('link', { name: 'Workspace' }).click();
  await page.getByLabel('File (relative to the workspace)').fill(file);
  await page.getByLabel('Search for').fill('beta');
  await page.getByLabel('Replace with').fill('BETA');
  await page.getByRole('button', { name: 'Apply edit' }).click();

  const diff = page.getByTestId('diff');
  await expect(diff).toContainText('-beta');
  await expect(diff).toContainText('+BETA');
});

test('approvals: a held command appears and can be approved', async ({ page, request }) => {
  const held = await request.post(`${API}/api/v1/workspace/commands`, {
    headers: { Authorization: `Bearer ${TOKEN}` },
    data: { command: 'hostname', shell: 'bash' },
  });
  expect(held.status()).toBe(202);

  await signIn(page);
  await page.getByRole('link', { name: 'Approvals' }).click();
  const items = page.locator('li', { hasText: 'hostname' });
  await expect(items.first()).toBeVisible();
  const before = await items.count();
  const item = items.first();


  await item.getByRole('button', { name: 'Approve' }).click();
  await expect(items).toHaveCount(before - 1);
});
