import fs from 'node:fs'
import path from 'node:path'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Use TLS certs from project root if they exist (for custom domain dev)
const certPath = path.resolve(__dirname, '../../cert.pem')
const keyPath = path.resolve(__dirname, '../../key.pem')
const hasCerts = fs.existsSync(certPath) && fs.existsSync(keyPath)

export default defineConfig({
  plugins: [react()],
  server: {
    host: '0.0.0.0',
    ...(hasCerts && {
      https: {
        cert: fs.readFileSync(certPath),
        key: fs.readFileSync(keyPath),
      },
    }),
    proxy: {
      '/api': {
        target: 'https://127.0.0.1:5001',
        changeOrigin: true,
        secure: false,
      },
      '/hubs': {
        target: 'https://127.0.0.1:5001',
        changeOrigin: true,
        secure: false,
        ws: true,
      },
    },
  },
})
