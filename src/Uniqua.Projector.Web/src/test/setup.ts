import '@testing-library/jest-dom/vitest'

import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// Each component test gets a clean document. Without this, a screen rendered by one test is still
// in the DOM for the next, and a query that should find nothing finds the previous test's markup.
afterEach(() => {
  cleanup()
})

// jsdom 26 has no PointerEvent (https://github.com/jsdom/jsdom/issues/2527). dnd-kit's
// PointerSensor reads `isPrimary`, `button` and the pointer coordinates off the event it receives
// from `fireEvent.pointerDown`/`pointerMove`/`pointerUp`; without a real PointerEvent those are all
// undefined and the sensor never starts a drag. This is a minimal polyfill of the constructor shape
// dnd-kit needs, not a production concern — every dnd-kit-driven component test relies on it.
if (typeof window !== 'undefined' && typeof window.PointerEvent === 'undefined') {
  class PointerEventPolyfill extends MouseEvent {
    public pointerId: number
    public isPrimary: boolean

    constructor(type: string, params: PointerEventInit = {}) {
      super(type, params)
      this.pointerId = params.pointerId ?? 0
      this.isPrimary = params.isPrimary ?? false
    }
  }

  Object.defineProperty(window, 'PointerEvent', { value: PointerEventPolyfill, writable: true })
  Object.defineProperty(globalThis, 'PointerEvent', { value: PointerEventPolyfill, writable: true })
}
