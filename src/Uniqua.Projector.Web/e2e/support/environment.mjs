// The one place the e2e run's moving parts are named, shared by playwright.config.ts, the launcher
// and the teardown so the three can never disagree about which container or address they mean.

/** The API's own address: it serves the built client from wwwroot, so this is the only origin. */
export const baseURL = process.env.E2E_BASE_URL ?? 'https://localhost:7299'

/** One container per run, removed before it starts and after it ends. */
export const sqlContainer = 'uniqua-projector-e2e-sql'

/**
 * The same pinned image `ApiFactory` starts for the integration tests, so the browser flows run
 * against the engine the application ships on — never an in-memory stand-in.
 */
export const sqlImage = 'mcr.microsoft.com/mssql/server:2022-latest'

/** Throwaway: the container lives for one run and listens on the loopback interface only. */
export const sqlPassword = 'Uniqua!E2e-Local1'
