import '@testing-library/jest-dom/vitest'

import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// Each component test gets a clean document. Without this, a screen rendered by one test is still
// in the DOM for the next, and a query that should find nothing finds the previous test's markup.
afterEach(() => {
  cleanup()
})
