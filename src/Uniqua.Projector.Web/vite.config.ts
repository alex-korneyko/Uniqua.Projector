import path from 'node:path'
// vitest/config re-exports Vite's own defineConfig, widened to accept the `test` block below —
// so there is still exactly one configuration and not a second one for tests to drift from.
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// The client is built into the API's wwwroot and served from the same origin the API answers on.
// That is not a convenience — the session cookie is httpOnly and same-origin, so splitting the two
// across hosts would break authentication (see docs/architecture-map.md § Constraints).
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
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: true,
    include: ['src/**/*.test.{ts,tsx}'],
  },
  build: {
    outDir: '../Uniqua.Projector.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    // Development runs on one origin too: everything the API owns is proxied to it.
    proxy: {
      '/api': { target: 'http://localhost:5199', changeOrigin: true },
      '/health': { target: 'http://localhost:5199', changeOrigin: true },
      '/hubs': { target: 'http://localhost:5199', changeOrigin: true, ws: true },
    },
  },
})
