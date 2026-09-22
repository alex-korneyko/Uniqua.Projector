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

/**
 * T44 — review 2026-09-22-2 N-13. `--color-input` and `--color-ring` were picked for their
 * position in a neutral ramp, not for how they read against the page: input measured about
 * 1.26:1 and ring about 2.6:1 against `--color-background`, both under WCAG 1.4.11's 3:1 floor
 * for a non-text UI component. This computes the ratio from the oklch values actually declared
 * in index.css, so a future edit that quietly redarkens the ramp fails here instead of in an
 * audit.
 */

interface Oklch {
  l: number
  c: number
  h: number
}

function parseOklch(raw: string): Oklch {
  const [l, c, h] = raw.trim().split(/\s+/).map(Number)
  return { l, c, h }
}

// One `--color-<name>: oklch(L C H)` declaration per theme block. `css` may contain the token
// more than once if a dark theme block later redefines it; every occurrence is checked.
function declarationsFor(name: string): Oklch[] {
  const pattern = new RegExp(`--color-${name}:\\s*oklch\\(([^)]+)\\)`, 'g')
  const found: Oklch[] = []
  for (const match of css.matchAll(pattern)) {
    found.push(parseOklch(match[1]))
  }
  return found
}

// Björn Ottosson's OKLab <-> linear-sRGB matrices (the reference the CSS `oklch()` colour
// function is itself defined against), composed with the WCAG relative-luminance weights.
function relativeLuminance({ l, c, h }: Oklch): number {
  const hRad = (h * Math.PI) / 180
  const a = c * Math.cos(hRad)
  const b = c * Math.sin(hRad)

  const l_ = l + 0.3963377774 * a + 0.2158037573 * b
  const m_ = l - 0.1055613458 * a - 0.0638541728 * b
  const s_ = l - 0.0894841775 * a - 1.291485548 * b

  const lCubed = l_ ** 3
  const mCubed = m_ ** 3
  const sCubed = s_ ** 3

  const rLin = 4.0767416621 * lCubed - 3.3077115913 * mCubed + 0.2309699292 * sCubed
  const gLin = -1.2684380046 * lCubed + 2.6097574011 * mCubed - 0.3413193965 * sCubed
  const bLin = -0.0041960863 * lCubed - 0.7034186147 * mCubed + 1.707614701 * sCubed

  const clamp = (channel: number) => Math.min(1, Math.max(0, channel))
  return 0.2126 * clamp(rLin) + 0.7152 * clamp(gLin) + 0.0722 * clamp(bLin)
}

// WCAG 2.x contrast ratio, lighter-over-darker.
function contrastRatio(a: Oklch, b: Oklch): number {
  const lumA = relativeLuminance(a)
  const lumB = relativeLuminance(b)
  const lighter = Math.max(lumA, lumB)
  const darker = Math.min(lumA, lumB)
  return (lighter + 0.05) / (darker + 0.05)
}

describe('input and ring token contrast (WCAG 1.4.11)', () => {
  const backgrounds = declarationsFor('background')
  expect(backgrounds.length).toBeGreaterThan(0)

  it.each(['input', 'ring'])('--color-%s reaches 3:1 against --color-background in every declared theme', (name) => {
    const declarations = declarationsFor(name)
    expect(declarations.length).toBeGreaterThan(0)

    // Pair each declaration with the background declared in the same theme block. There is
    // exactly one background today (light only); if a dark block is added later, both sides
    // gain a second declaration in the same order and this still pairs them correctly.
    declarations.forEach((token, index) => {
      const background = backgrounds[Math.min(index, backgrounds.length - 1)]
      const ratio = contrastRatio(token, background)
      expect(ratio).toBeGreaterThanOrEqual(3)
    })
  })
})
