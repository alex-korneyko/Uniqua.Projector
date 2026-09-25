/**
 * SCR-04 — one board, at `/boards/:boardId`. A placeholder the route table points at, so T20 fills
 * it in without editing `app/routes.tsx`. It is only ever rendered for a recognised account: a
 * visitor at this address is shown the sign-in form and no board request is made (AC-27).
 */
export function BoardScreen() {
  return <p className="text-muted-foreground text-sm">The board arrives in a later step.</p>
}
