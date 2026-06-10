import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Proxy /api to the .NET backend so the SPA and API share an origin in dev.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5180',
    },
  },
})
