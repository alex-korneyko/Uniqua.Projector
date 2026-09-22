import path from 'node:path'
// vitest/config re-exports Vite's own defineConfig, widened to accept the `test` block below —
// so there is still exactly one configuration and not a second one for tests to drift from.
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// The client is built into the API's wwwroot and served from the same origin the API answers on.
// That is not a convenience — the session cookie is httpOnly and same-origin, so splitting the two
// across hosts would break authentication (see docs/architecture-map.md § Constraints).

// The API's development profile, which is HTTPS and not negotiable: the antiforgery cookie carries
// the `__Host-` prefix and the session cookie is Secure, and the framework's antiforgery throws
// rather than issue either one over a plain connection — so an http target answers 500 to every
// request, including the static client. Kept beside the launch profile's applicationUrl in
// src/Uniqua.Projector.Api/Properties/launchSettings.json; the two are one decision.
const apiTarget = 'https://localhost:7199'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, './src') },
  },
  // Component tests run in this same config, so a test resolves '@/...' and processes CSS exactly
  // as the built client does. A second, parallel build configuration for tests is a place for the
  // two to disagree.
  test: {
    environment: 'jsdom',
    // No globals: every test imports describe/it/expect from 'vitest', so none is ambient.
    setupFiles: ['./src/test/setup.ts'],
    css: true,
    include: ['src/**/*.test.{ts,tsx}'],
  },
  build: {
    outDir: '../Uniqua.Projector.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    // Development runs on one origin too: everything the API owns is proxied to it. `secure: false`
    // is about this proxy's own trust store, not the browser's — Node does not trust the ASP.NET
    // development certificate, and refusing it here would fail every call locally.
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true, secure: false },
      '/health': { target: apiTarget, changeOrigin: true, secure: false },
      '/hubs': { target: apiTarget, changeOrigin: true, secure: false, ws: true },
    },
  },
})
