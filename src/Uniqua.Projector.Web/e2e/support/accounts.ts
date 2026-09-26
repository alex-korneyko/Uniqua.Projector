import { expect, type Page } from '@playwright/test'

export interface TestAccount {
  email: string
  password: string
  displayName: string
}

/** A fresh account per call: the database lives for one run, but each flow keeps its own people. */
export function anAccount(label: string): TestAccount {
  const unique = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  return {
    email: `${label}-${unique}@example.test`,
    password: 'a long enough password',
    displayName: `E2E ${label}`,
  }
}

/** Registers through the visitor screens, from the sign-in form every visitor is met by. */
export async function register(page: Page, account: TestAccount): Promise<void> {
  await page.getByRole('button', { name: /no account yet/i }).click()
  await page.getByLabel('Email address').fill(account.email)
  await page.getByLabel('Password', { exact: true }).fill(account.password)
  await page.getByLabel('Display name').fill(account.displayName)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByText(account.displayName, { exact: true })).toBeVisible()
}

export async function signIn(page: Page, account: TestAccount): Promise<void> {
  await page.getByLabel('Email address').fill(account.email)
  await page.getByLabel('Password', { exact: true }).fill(account.password)
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
}

export async function signOut(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Sign out' }).click()
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible()
}

/** Creates a board from the board list, and waits for it to open at its own address. */
export async function createBoard(page: Page, name: string): Promise<string> {
  await page.getByRole('button', { name: 'Create board' }).click()
  await page.getByLabel('Board name').fill(name)
  await page.getByRole('button', { name: 'Create', exact: true }).click()
  await expect(page).toHaveURL(/\/boards\/[0-9a-f-]{36}$/)
  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible()
  return new URL(page.url()).pathname
}
