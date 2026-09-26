import { execFileSync } from 'node:child_process'

import { sqlContainer } from './environment.mjs'

/** Removes the run's SQL Server container, whatever state the launcher was stopped in. */
export default function globalTeardown() {
  try {
    execFileSync('docker', ['rm', '-f', sqlContainer], { stdio: 'ignore' })
  } catch {
    // Already gone.
  }
}
