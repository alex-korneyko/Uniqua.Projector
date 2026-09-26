// Playwright's `webServer`: brings the whole application up on one origin, as it ships.
//
//   1. a fresh SQL Server container, from the image `ApiFactory` pins;
//   2. the client built into the API's wwwroot (`npm run build`);
//   3. the schema brought up through the EF Core migrations — the explicit step deployment runs;
//   4. the API over HTTPS in Development, serving the client and answering /api on the same origin,
//      so the httpOnly session cookie and the antiforgery token behave exactly as in production.
//
// Playwright waits for `${baseURL}/health`, runs the specs, then stops this process; the container
// is removed by global-teardown.ts, since a killed process cannot be relied on to clean up after
// itself on every platform.

import { execFileSync, execSync, spawn } from 'node:child_process'
import path from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'

import { baseURL, sqlContainer, sqlImage, sqlPassword } from './environment.mjs'

const webDir = path.resolve(import.meta.dirname, '..', '..')
const repoRoot = path.resolve(webDir, '..', '..')
const apiProject = path.join(repoRoot, 'src', 'Uniqua.Projector.Api')
const infrastructureProject = path.join(repoRoot, 'src', 'Uniqua.Projector.Infrastructure')

function run(command, args, options = {}) {
  execFileSync(command, args, { stdio: 'inherit', ...options })
}

/** npm is a .cmd script on Windows, which Node starts only through a shell: one fixed command line. */
function runNpmBuild() {
  execSync('npm run build', { cwd: webDir, stdio: 'inherit' })
}

function read(command, args) {
  return execFileSync(command, args, { encoding: 'utf8' }).trim()
}

function removeContainer() {
  try {
    execFileSync('docker', ['rm', '-f', sqlContainer], { stdio: 'ignore' })
  } catch {
    // Nothing to remove.
  }
}

async function startSqlServer() {
  removeContainer()
  run('docker', [
    'run', '--detach', '--name', sqlContainer,
    '--env', 'ACCEPT_EULA=Y',
    '--env', `MSSQL_SA_PASSWORD=${sqlPassword}`,
    // A port Docker picks, on loopback only: other runs (and their containers) may hold any other.
    '--publish', '127.0.0.1::1433',
    sqlImage,
  ])

  const published = read('docker', ['port', sqlContainer, '1433/tcp']).split(/\r?\n/)[0]
  const port = published.slice(published.lastIndexOf(':') + 1)

  const deadline = Date.now() + 180_000
  while (!read('docker', ['logs', sqlContainer]).includes('SQL Server is now ready for client connections')) {
    if (Date.now() > deadline) {
      throw new Error(`SQL Server in ${sqlContainer} did not become ready within 180 seconds.`)
    }
    await delay(1_000)
  }

  return `Server=127.0.0.1,${port};Database=UniquaProjectorE2e;User Id=sa;Password=${sqlPassword};TrustServerCertificate=True`
}

/** Through the migrations, as deployment does — retried while the engine finishes starting up. */
async function migrate(connectionString) {
  const environment = { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development' }
  for (let attempt = 1; ; attempt += 1) {
    try {
      run(
        'dotnet',
        [
          'ef', 'database', 'update',
          '--no-build',
          '--project', infrastructureProject,
          '--startup-project', apiProject,
          '--connection', connectionString,
        ],
        { env: environment },
      )
      return
    } catch (error) {
      if (attempt === 3) {
        throw error
      }
      await delay(5_000)
    }
  }
}

const connectionString = await startSqlServer()
runNpmBuild()
// One build (with its restore) for both the migration step and the API run that follow.
run('dotnet', ['build', apiProject, '--nologo', '--verbosity', 'quiet'])
await migrate(connectionString)

const api = spawn(
  'dotnet',
  ['run', '--no-build', '--no-launch-profile', '--project', apiProject, '--urls', baseURL],
  {
    stdio: 'inherit',
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ConnectionStrings__Default: connectionString,
    },
  },
)

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => {
    api.kill()
    removeContainer()
    process.exit(0)
  })
}

api.on('exit', (code) => {
  removeContainer()
  process.exit(code ?? 1)
})
