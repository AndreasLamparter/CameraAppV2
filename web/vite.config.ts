import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'
import ui from '@nuxt/ui/vite'
import { fileURLToPath, URL } from 'node:url'

// Backend for the dev proxy (never port 5080: the old application). Override with TIMING_API if needed.
const api = process.env.TIMING_API ?? 'http://localhost:5081'

export default defineConfig({
  plugins: [
    vue(),
    ui({
      // Offline finish PC: icons used in the source are bundled, nothing is fetched at runtime.
      icon: { clientBundle: { scan: true } },
      ui: { colors: { primary: 'blue', neutral: 'slate' } },
    }),
  ],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: api, changeOrigin: true },
      '/hubs': { target: api, changeOrigin: true, ws: true },
      '/health': { target: api, changeOrigin: true },
    },
  },
  build: {
    outDir: '../src/TimingApp.Api/wwwroot',
    emptyOutDir: true,
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.spec.ts'],
  },
})
