import path from 'node:path'
import { defineConfig } from 'vite'
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
