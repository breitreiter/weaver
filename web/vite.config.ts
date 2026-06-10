import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Proxy /api to the .NET backend so the SPA and API share an origin in dev.
// Override the target with WEAVER_API_PROXY (default :5180).
const apiTarget = process.env.WEAVER_API_PROXY ?? 'http://localhost:5180'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': apiTarget,
    },
  },
})
