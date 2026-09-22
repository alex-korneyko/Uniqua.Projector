import { useQuery } from '@tanstack/react-query'

import { Button } from '@/components/ui/button'

/**
 * The skeleton's placeholder screen. It exists to prove the client is wired end to end — Tailwind
 * styles it, a vendored shadcn/ui primitive renders it, and TanStack Query reaches the API on the
 * same origin. The first real board screen replaces it.
 */
export default function App() {
  const health = useQuery({
    queryKey: ['health'],
    queryFn: async () => {
      const response = await fetch('/health')
      if (!response.ok) throw new Error('The API did not answer.')
      return (await response.json()) as { status: string }
    },
  })

  return (
    <main className="mx-auto flex min-h-dvh max-w-md flex-col items-start gap-4 p-8">
      <h1 className="text-2xl font-semibold">Uniqua Projector</h1>
      <p className="text-muted-foreground text-sm">
        API status: {health.isPending ? 'checking…' : (health.data?.status ?? 'unreachable')}
      </p>
      <Button onClick={() => void health.refetch()}>Check again</Button>
    </main>
  )
}
