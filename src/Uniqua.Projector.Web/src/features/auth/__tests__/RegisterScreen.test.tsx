import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { RegisterScreen } from '@/features/auth/RegisterScreen'

/**
 * T16 — the registration screen in every state the contract can put it in.
 *
 * The assertion running through almost all of them is that nothing the visitor typed is lost. A
 * visitor who has to retype three fields because one of them collided is the failure KPI 1
 * measures, and the password is the field it is most tempting — and most costly — to clear.
 */

const submitted = {
  email: 'someone@example.test',
  password: 'a-long-enough-password',
  displayName: 'Someone Real',
}

function renderScreen() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })

  return render(
    <QueryClientProvider client={client}>
      <RegisterScreen />
    </QueryClientProvider>,
  )
}

function problem(status: number, body: unknown) {
  return {
    ok: false,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
    headers: new Headers({ 'content-type': 'application/problem+json' }),
  } as unknown as Response
}

function created() {
  return {
    ok: true,
    status: 201,
    json: async () => ({
      id: '018f3a2b-7c4d-7e91-a0b2-3c4d5e6f7a8b',
      email: submitted.email,
      display_name: submitted.displayName,
    }),
    text: async () => '',
    headers: new Headers({ 'content-type': 'application/json' }),
  } as unknown as Response
}

const fetchMock = vi.fn()

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
  document.cookie = 'XSRF-TOKEN=a-token'
})

afterEach(() => {
  vi.unstubAllGlobals()
})

function emailField() {
  return screen.getByLabelText(/email/i)
}

function passwordField() {
  return screen.getByLabelText(/password/i)
}

function displayNameField() {
  return screen.getByLabelText(/display name/i)
}

async function fillAndSubmit() {
  await userEvent.type(emailField(), submitted.email)
  await userEvent.type(passwordField(), submitted.password)
  await userEvent.type(displayNameField(), submitted.displayName)
  await userEvent.click(screen.getByRole('button', { name: /create account/i }))
}

function expectEverythingKept() {
  expect(emailField()).toHaveValue(submitted.email)
  // The password included. Clearing it is the habit this assertion exists to prevent.
  expect(passwordField()).toHaveValue(submitted.password)
  expect(displayNameField()).toHaveValue(submitted.displayName)
}

// ---- 1. the default state -----------------------------------------------------------------------

describe('the default state', () => {
  it('offers the three fields the contract asks for, and no error', () => {
    renderScreen()

    expect(emailField()).toHaveValue('')
    expect(passwordField()).toHaveValue('')
    expect(displayNameField()).toHaveValue('')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('does not show the password as it is typed', async () => {
    renderScreen()

    expect(passwordField()).toHaveAttribute('type', 'password')
  })
})

// ---- 2. the submitting state --------------------------------------------------------------------

describe('while the submission is in flight', () => {
  it('cannot be submitted a second time', async () => {
    // A double click must not be able to create two accounts, which the server would refuse on the
    // second anyway — but with a confusing "that address is taken" against the visitor's own.
    fetchMock.mockReturnValue(new Promise(() => {}))

    renderScreen()
    await fillAndSubmit()

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /creating|create account/i })).toBeDisabled(),
    )

    await userEvent.click(screen.getByRole('button', { name: /creating|create account/i }))
    expect(fetchMock).toHaveBeenCalledOnce()
  })
})

// ---- 3. a field was not usable (AC-02, AC-02b) --------------------------------------------------

describe('when a submitted value is not usable', () => {
  it('shows the password refusal and keeps everything typed', async () => {
    fetchMock.mockResolvedValue(
      problem(400, {
        code: 'accounts.password_invalid',
        detail: 'A password must be at least 8 characters long.',
      }),
    )

    renderScreen()
    await fillAndSubmit()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'A password must be at least 8 characters long.',
    )
    expectEverythingKept()
  })

  it('shows the address refusal and keeps everything typed', async () => {
    fetchMock.mockResolvedValue(
      problem(400, {
        code: 'accounts.email_invalid',
        detail: 'That is not an email address we can use.',
      }),
    )

    renderScreen()
    await fillAndSubmit()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'That is not an email address we can use.',
    )
    expectEverythingKept()
  })

  it('puts focus on the field the refusal was about', async () => {
    // So a visitor can correct it without hunting for which of three fields the server meant.
    fetchMock.mockResolvedValue(
      problem(400, {
        code: 'accounts.email_invalid',
        detail: 'That is not an email address we can use.',
      }),
    )

    renderScreen()
    await fillAndSubmit()

    await waitFor(() => expect(emailField()).toHaveFocus())
  })
})

// ---- 4. something was already taken (AC-03, AC-11b) ---------------------------------------------

describe('when the address or the name is already taken', () => {
  it('says which one collided, and keeps everything typed', async () => {
    // Naming the field is deliberate here (spec §6.1): a visitor cannot fix a collision they are
    // not told about, and the address being registered is something they may well want to know.
    fetchMock.mockResolvedValue(
      problem(409, {
        code: 'accounts.email_taken',
        detail: 'An email address identifies exactly one account.',
      }),
    )

    renderScreen()
    await fillAndSubmit()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'An email address identifies exactly one account.',
    )
    expectEverythingKept()
  })

  it('says so for a taken display name too, and keeps everything typed', async () => {
    fetchMock.mockResolvedValue(
      problem(409, {
        code: 'accounts.display_name_taken',
        detail: 'A display name identifies exactly one account to the people who see it.',
      }),
    )

    renderScreen()
    await fillAndSubmit()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'A display name identifies exactly one account to the people who see it.',
    )
    expectEverythingKept()
    await waitFor(() => expect(displayNameField()).toHaveFocus())
  })

  it('invents no second message when both could have collided', async () => {
    fetchMock.mockResolvedValue(
      problem(409, {
        code: 'accounts.email_taken',
        detail: 'An email address identifies exactly one account.',
      }),
    )

    renderScreen()
    await fillAndSubmit()

    await screen.findByRole('alert')
    expect(screen.getAllByRole('alert')).toHaveLength(1)
  })
})

// ---- 5. registration is temporarily limited (AC-01b) --------------------------------------------

describe('when registration is temporarily limited', () => {
  it('names the wait in seconds rather than paraphrasing it away', async () => {
    fetchMock.mockResolvedValue(
      problem(429, {
        code: 'accounts.registration_rate_limited',
        detail: 'Too many accounts have been created from here in the past minute.',
        retry_after_seconds: 37,
      }),
    )

    renderScreen()
    await fillAndSubmit()

    const message = await screen.findByRole('alert')
    expect(message).toHaveTextContent(/37 seconds/)
    expectEverythingKept()
  })

  it('still says something useful when the server sent no number', async () => {
    fetchMock.mockResolvedValue(
      problem(429, {
        code: 'accounts.registration_rate_limited',
        detail: 'Too many accounts have been created from here in the past minute.',
      }),
    )

    renderScreen()
    await fillAndSubmit()

    const message = await screen.findByRole('alert')
    expect(message.textContent).toMatch(/temporarily limited|try again/i)
    expect(message.textContent).not.toMatch(/undefined|NaN|null/)
  })
})

// ---- 6. the account was created (AC-01) ---------------------------------------------------------

describe('when the account is created', () => {
  it('leaves the client signed in and never shows a sign-in form', async () => {
    fetchMock.mockImplementation((_: RequestInfo | URL, init?: RequestInit) =>
      Promise.resolve(init?.method === 'POST' ? created() : problem(401, {})),
    )

    renderScreen()
    await fillAndSubmit()

    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    expect(screen.queryByText(/sign in/i)).not.toBeInTheDocument()
  })
})

// ---- The states the contract does not enumerate -------------------------------------------------

describe('when something unexpected happens', () => {
  it('shows one plain message and keeps the form, never a raw code', async () => {
    fetchMock.mockResolvedValue(problem(502, { nothing: 'useful' }))

    renderScreen()
    await fillAndSubmit()

    const message = await screen.findByRole('alert')
    expect(message.textContent).not.toMatch(/502|accounts\.|undefined/)
    expect(message.textContent).toMatch(/\w+/)
    expectEverythingKept()
  })

  it('makes no claim about whether the account exists when the network dies', async () => {
    // The request may or may not have reached the server. Saying either is a guess.
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'))

    renderScreen()
    await fillAndSubmit()

    const message = await screen.findByRole('alert')
    expect(message.textContent).not.toMatch(/created|was not created/i)
    expectEverythingKept()
    expect(screen.getByRole('button', { name: /create account/i })).toBeEnabled()
  })

  it('asks for a plain retry when the request could not be verified', async () => {
    // Not a validation message on a field: nothing the visitor typed was wrong.
    fetchMock.mockResolvedValue(
      problem(403, {
        code: 'accounts.antiforgery_failed',
        detail: 'The request could not be verified as coming from this application.',
      }),
    )

    renderScreen()
    await fillAndSubmit()

    const message = await screen.findByRole('alert')
    expect(message.textContent).toMatch(/try again/i)
    expectEverythingKept()
  })
})
