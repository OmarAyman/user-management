import { Page, expect } from '@playwright/test';

/** The seeded demo accounts, matching the README and the backend test constants. */
export const Credentials = {
  admin: { username: 'admin', password: 'Admin@123456' },
  user: { username: 'jdoe', password: 'User@1234567' },
  readOnly: { username: 'readonly', password: 'ReadOnly@1234' },
} as const;

/**
 * Signs in through the form.
 *
 * Through the UI rather than by injecting a session, because sign-in is one of the things this suite exists to
 * prove.
 */
export async function signIn(page: Page, who: keyof typeof Credentials): Promise<void> {
  const { username, password } = Credentials[who];

  await page.goto('/login');
  await page.getByLabel('Username').fill(username);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();

  await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible();
}

/**
 * Calls the API from the page context with its own token.
 *
 * It cannot borrow the application's token, and that is the design working as intended: the access token lives
 * in an Angular signal, so no script in the page - helper or attacker - can read it out of storage. Setup
 * traffic therefore signs in for itself.
 */
export async function apiRequest(
  page: Page,
  method: string,
  path: string,
  body?: unknown,
  as: keyof typeof Credentials = 'admin',
): Promise<{ status: number; body: string }> {
  const { username, password } = Credentials[as];

  return page.evaluate(
    async ([httpMethod, url, payload, user, secret]) => {
      const signIn = await fetch('/api/auth/login', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: user, password: secret }),
      });

      const { accessToken } = (await signIn.json()) as { accessToken: string };

      const response = await fetch(url as string, {
        method: httpMethod as string,
        credentials: 'include',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${accessToken}`,
        },
        body: payload === null ? undefined : JSON.stringify(payload),
      });

      return { status: response.status, body: await response.text() };
    },
    [method, path, body ?? null, username, password] as const,
  );
}

/** Creates a user through the API. Setup data is created, not clicked - except where the form is the subject. */
export async function createUserViaApi(
  page: Page,
  username: string,
  roleId = 2,
): Promise<{ id: string; username: string }> {
  const response = await apiRequest(page, 'POST', '/api/users', {
    username,
    email: `${username}@example.com`,
    firstName: 'Smoke',
    lastName: 'Test',
    password: 'Created@123456',
    roleId,
  });

  if (response.status !== 201) {
    throw new Error(`Creating ${username} failed with ${response.status}: ${response.body}`);
  }

  return JSON.parse(response.body) as { id: string; username: string };
}

export function uniqueName(prefix: string): string {
  return `${prefix}${Date.now().toString().slice(-7)}`;
}
