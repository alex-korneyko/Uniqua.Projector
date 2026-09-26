import { expect, test, type Browser, type Page } from '@playwright/test'

import { anAccount, createBoard, register, signIn, signOut, type TestAccount } from './support/accounts'

/**
 * test-plan.md — AC-27, e2e-through-UI: a visitor following a board link signs in and lands back at
 * that address. A member is shown the board; a non-member and an invented id are shown the
 * board-not-available screen; before sign-in, nothing on the page differs between the three.
 */
test('a visitor opening a board link signs in and is answered at that address', async ({ browser }) => {
  const owner = anAccount('link-owner')
  const outsider = anAccount('link-outsider')
  const boardName = 'Linked board'

  // Arrange: the owner makes a board; the outsider exists but is not a member of it.
  const boardPath = await asNewVisitor(browser, async (page) => {
    await page.goto('/')
    await register(page, owner)
    const path = await createBoard(page, boardName)
    await signOut(page)
    return path
  })
  await asNewVisitor(browser, async (page) => {
    await page.goto('/')
    await register(page, outsider)
    await signOut(page)
  })
  const inventedPath = '/boards/0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a00'

  const cases: { who: string; path: string; account: TestAccount; answer: 'board' | 'not-available' }[] = [
    { who: 'a member', path: boardPath, account: owner, answer: 'board' },
    { who: 'a non-member', path: boardPath, account: outsider, answer: 'not-available' },
    { who: 'anyone, at an invented id', path: inventedPath, account: owner, answer: 'not-available' },
  ]

  const beforeSignIn: string[] = []

  for (const { who, path, account, answer } of cases) {
    await test.step(who, async () => {
      await asNewVisitor(browser, async (page) => {
        await page.goto(path)

        // The ordinary sign-in form, revealing nothing about the board.
        await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible()
        await expect(page.getByText(boardName)).toHaveCount(0)
        beforeSignIn.push(await page.locator('body').innerText())

        await signIn(page, account)

        // Back at the link's address, answered there as any signed-in account is.
        await expect(page).toHaveURL(path)
        if (answer === 'board') {
          await expect(page.getByRole('heading', { level: 1, name: boardName })).toBeVisible()
        } else {
          await expect(page.getByRole('heading', { name: 'Board not available' })).toBeVisible()
          await expect(page.getByText(boardName)).toHaveCount(0)
        }
      })
    })
  }

  // A real board, the same board for someone else, and a board that never existed: one page.
  expect(new Set(beforeSignIn).size).toBe(1)
})

/** A browser context of its own: no cookies, so whoever uses it starts as a visitor. */
async function asNewVisitor<T>(browser: Browser, flow: (page: Page) => Promise<T>): Promise<T> {
  const context = await browser.newContext()
  try {
    return await flow(await context.newPage())
  } finally {
    await context.close()
  }
}
