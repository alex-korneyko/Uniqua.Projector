import { render, screen } from '@testing-library/react'
import { readFileSync, readdirSync } from 'node:fs'
import path from 'node:path'
import { describe, expect, it } from 'vitest'

import { PlainText } from '@/features/boards/PlainText'

/**
 * T16 / AC-16 — a card title or description containing markup, a script tag, or a bare URL is
 * shown as the exact characters typed, and nothing in it is ever run or rendered as formatting:
 * React text nodes only, never `dangerouslySetInnerHTML`, never linkified (screens.md §New
 * components, "React text nodes only, whitespace-pre-wrap break-words, never
 * dangerouslySetInnerHTML, never linkified").
 */

describe('PlainText', () => {
  it('renders markup as literal characters, with no element created from it', () => {
    render(<PlainText text="<b>not bold</b>" />)

    expect(screen.getByText('<b>not bold</b>')).toBeInTheDocument()
    expect(document.querySelector('b')).not.toBeInTheDocument()
  })

  it('renders a script tag as literal text, never executed or inserted as an element', () => {
    render(<PlainText text="<script>window.__pwned = true</script>" />)

    expect(document.querySelector('script')).toBeNull()
    expect((window as typeof window & { __pwned?: boolean }).__pwned).toBeUndefined()
    expect(screen.getByText('<script>window.__pwned = true</script>')).toBeInTheDocument()
  })

  it('renders a web address as plain text, never as a link', () => {
    render(<PlainText text="https://example.com" />)

    expect(screen.getByText('https://example.com')).toBeInTheDocument()
    expect(document.querySelector('a')).not.toBeInTheDocument()
  })

  it('keeps line breaks and repeated spaces visible (white-space: pre-wrap)', () => {
    const { container } = render(<PlainText text={'first line\nsecond  line'} />)

    const node = container.firstElementChild as HTMLElement
    expect(node).not.toBeNull()
    expect(node.textContent).toBe('first line\nsecond  line')
    expect(node.className).toContain('whitespace-pre-wrap')
    expect(node.className).toContain('break-words')
  })

  it('renders exactly one text node and nothing else — no innerHTML, no child elements', () => {
    const { container } = render(<PlainText text="<img src=x onerror=alert(1)>" />)

    const node = container.firstElementChild as HTMLElement
    expect(node.children.length).toBe(0)
    expect(node.textContent).toBe('<img src=x onerror=alert(1)>')
  })
})

describe('features/boards never uses dangerouslySetInnerHTML', () => {
  it('has no occurrence of dangerouslySetInnerHTML in any source file under features/boards', () => {
    const boardsDir = path.resolve(import.meta.dirname, '..')
    const offenders: string[] = []

    const walk = (dir: string) => {
      for (const entry of readdirSync(dir, { withFileTypes: true })) {
        if (entry.name === '__tests__') continue
        const fullPath = path.join(dir, entry.name)
        if (entry.isDirectory()) {
          walk(fullPath)
        } else if (/\.(ts|tsx)$/.test(entry.name)) {
          const contents = readFileSync(fullPath, 'utf-8')
          // Matches the JSX attribute usage only (`dangerouslySetInnerHTML={...}` or `="..."`),
          // not a mention of the name in a comment explaining why it must not appear.
          if (/dangerouslySetInnerHTML\s*=/.test(contents)) {
            offenders.push(fullPath)
          }
        }
      }
    }

    walk(boardsDir)

    expect(offenders).toEqual([])
  })
})
