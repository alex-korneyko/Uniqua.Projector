import { expect, test, type Page } from '@playwright/test'

import { anAccount, createBoard, register } from './support/accounts'

/**
 * test-plan.md — AC-01 + AC-12, e2e-through-UI: a fresh account reaches a board holding a card
 * unaided. Register → create a board → add a card; the card is shown last in its column and is
 * still there after a reload, because the server kept it, not the page.
 */
test('a fresh account registers, creates a board, adds a card, and finds it there after a reload', async ({
  page,
}) => {
  await page.goto('/')
  await register(page, anAccount('thin-path'))

  // AC-01: the board opens at once, with To do, In progress and Done in that order.
  await expect(page.getByRole('heading', { name: 'My boards' })).toBeVisible()
  await createBoard(page, 'Thin path board')
  await expect(page.getByRole('heading', { level: 2 })).toHaveText(['To do', 'In progress', 'Done'])

  // AC-12: a card added to To do lands last in that column and shows its title.
  const toDo = columnNamed(page, 'To do')
  await toDo.getByRole('button', { name: '+ Add card' }).click()
  await toDo.getByLabel('Title', { exact: true }).fill('First card')
  await toDo.getByRole('button', { name: 'Add', exact: true }).click()
  await expect(toDo.getByRole('button', { name: 'First card' })).toBeVisible()
  await toDo.getByLabel('Title', { exact: true }).fill('Second card')
  await toDo.getByRole('button', { name: 'Add', exact: true }).click()
  await expect(toDo.getByRole('button', { name: 'Second card' })).toBeVisible()

  await page.reload()

  await expect(page.getByRole('heading', { level: 1, name: 'Thin path board' })).toBeVisible()
  // Both cards, in the order they were added: the second last in its column.
  await expect(columnNamed(page, 'To do').getByRole('listitem')).toHaveText(['First card', 'Second card'])
})

/** A column on SCR-04: the section headed by its name. */
function columnNamed(page: Page, name: string) {
  return page.locator('section').filter({ has: page.getByRole('heading', { level: 2, name, exact: true }) })
}
