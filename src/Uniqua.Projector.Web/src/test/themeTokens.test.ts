import { describe, expect, it } from 'vitest'

import css from '@/index.css?raw'

/**
 * T32 — review 2026-09-22 R-19. A Tailwind utility that names a theme colour Tailwind was never
 * told about generates no CSS at all, silently: `text-destructive` without `--color-destructive`
 * renders a refusal in body colour and nobody notices. The vendored components read the shadcn
 * token names, so every one they use has to be defined in index.css.
 */

// Read through Vite itself rather than the file system, so the check needs nothing beyond the
// toolchain the client already builds with.
const sources = import.meta.glob<string>(['/src/**/*.{ts,tsx}', '!/src/**/*.test.{ts,tsx}'], {
  query: '?raw',
  import: 'default',
  eager: true,
})

const tokenNames = [
  'background', 'foreground', 'primary', 'primary-foreground', 'secondary', 'secondary-foreground',
  'muted', 'muted-foreground', 'accent', 'accent-foreground', 'destructive', 'destructive-foreground',
  'card', 'card-foreground', 'popover', 'popover-foreground', 'border', 'input', 'ring',
]

// Longest first, so card-foreground is not read as card followed by a word boundary.
const alternatives = [...tokenNames].sort((a, b) => b.length - a.length).join('|')
const utility = new RegExp(
  `\\b(?:bg|text|border|ring|outline|fill|stroke|ring-offset)-(${alternatives})\\b`,
  'g',
)

describe('the theme', () => {
  it('defines every colour token a component uses', () => {
    const used = new Set<string>()
    for (const source of Object.values(sources)) {
      for (const match of source.matchAll(utility)) {
        used.add(match[1])
      }
    }

    const undefinedTokens = [...used].filter((name) => !css.includes(`--color-${name}:`)).sort()

    expect(used.size).toBeGreaterThan(0)
    expect(undefinedTokens).toEqual([])
  })
})
