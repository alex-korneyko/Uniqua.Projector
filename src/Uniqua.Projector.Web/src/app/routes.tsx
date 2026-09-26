import { Navigate, type RouteObject } from 'react-router'

import { BoardListScreen } from '@/features/boards/BoardListScreen'
import { BoardScreen } from '@/features/boards/BoardScreen'

/**
 * ADR 0012: React Router in library mode — routing only. No loaders and no actions, so TanStack
 * Query stays the only owner of server state. Rendered (through `useRoutes`) only for a recognised
 * account; a visitor at any of these addresses is shown the sign-in form instead, and the address
 * waits as `returnTo`. Kept as data rather than a component so this file can also own the guard.
 */
export const appRoutes: RouteObject[] = [
  { path: '/', element: <BoardListScreen /> },
  { path: '/boards/:boardId', element: <BoardScreen /> },
  { path: '*', element: <Navigate to="/" replace /> },
]

/**
 * The `returnTo` guard (sad.md §8): true only for a path inside this application — a single leading
 * `/`, not `//` or `/\` (both of which a browser reads as another host), and no control characters
 * (which a browser strips, turning `/\t/evil` into `//evil`). A scheme cannot follow a leading `/`.
 */
export function isInAppPath(path: string): boolean {
  return (
    path.startsWith('/')
    && !path.startsWith('//')
    && !path.startsWith('/\\')
    && !hasControlCharacter(path)
  )
}

function hasControlCharacter(path: string): boolean {
  for (const character of path) {
    const code = character.charCodeAt(0)
    if (code < 0x20 || code === 0x7f) {
      return true
    }
  }
  return false
}
