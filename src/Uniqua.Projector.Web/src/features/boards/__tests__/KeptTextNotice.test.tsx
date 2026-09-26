import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { KeptTextNotice } from '@/features/boards/KeptTextNotice'

/**
 * T16 / AC-16, AC-18b, AC-28 — kept typed text stays visible, selectable plain text (never
 * re-rendered as an editable control) when the control that held it is gone, either because the
 * session ended (screens.md §SCR-04, "kept-text offer": «You were signed out before this was
 * saved:», «Apply again» / «Discard») or because the column/card it belonged to no longer exists
 * (screens.md §SCR-04, "kept-text gone": «That column no longer exists.» / «That card no longer
 * exists.», «Dismiss»).
 */

describe('KeptTextNotice — kept-text offer', () => {
  it('shows the kept text as selectable plain text with Apply again and Discard', () => {
    const onApplyAgain = vi.fn()
    const onDiscard = vi.fn()

    render(
      <KeptTextNotice
        heading="You were signed out before this was saved:"
        fields={[{ label: 'Title', value: '<b>Ship the release</b>' }]}
        onApplyAgain={onApplyAgain}
        onDiscard={onDiscard}
      />,
    )

    expect(screen.getByText('You were signed out before this was saved:')).toBeInTheDocument()

    // The kept text is rendered as plain text (PlainText), not inside an <input>/<textarea>, so it
    // can be selected and copied but never edited in place.
    expect(screen.getByText('<b>Ship the release</b>')).toBeInTheDocument()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()

    expect(screen.queryByRole('button', { name: /dismiss/i })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /apply again/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /discard/i })).toBeInTheDocument()
  })

  it('calls onApplyAgain and onDiscard when their buttons are clicked', async () => {
    const onApplyAgain = vi.fn()
    const onDiscard = vi.fn()
    const user = userEvent.setup()

    render(
      <KeptTextNotice
        heading="You were signed out before this was saved:"
        fields={[{ label: 'Title', value: 'Ship the release' }]}
        onApplyAgain={onApplyAgain}
        onDiscard={onDiscard}
      />,
    )

    await user.click(screen.getByRole('button', { name: /apply again/i }))
    expect(onApplyAgain).toHaveBeenCalledTimes(1)

    await user.click(screen.getByRole('button', { name: /discard/i }))
    expect(onDiscard).toHaveBeenCalledTimes(1)
  })
})

describe('KeptTextNotice — kept-text gone', () => {
  it('shows the gone message, the kept text as plain text, and only Dismiss', async () => {
    const onDismiss = vi.fn()
    const user = userEvent.setup()

    render(
      <KeptTextNotice
        heading="That column no longer exists."
        fields={[{ label: 'Description', value: 'https://example.com' }]}
        onDismiss={onDismiss}
      />,
    )

    expect(screen.getByText('That column no longer exists.')).toBeInTheDocument()
    expect(screen.getByText('https://example.com')).toBeInTheDocument()
    expect(document.querySelector('a')).not.toBeInTheDocument()

    expect(screen.queryByRole('button', { name: /apply again/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /discard/i })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /dismiss/i }))
    expect(onDismiss).toHaveBeenCalledTimes(1)
  })
})
