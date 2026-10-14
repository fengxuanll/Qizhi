import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

const root = fileURLToPath(new URL('..', import.meta.url))

export default defineConfig(({ command }) => ({
  plugins: [vue()],
  server: {
    port: 5173,
    strictPort: true,
    https:
      command === 'serve'
        ? {
            cert: readFileSync(resolve(root, 'work/certs/localhost.pem')),
            key: readFileSync(resolve(root, 'work/certs/localhost.key')),
          }
        : undefined,
    proxy: Object.fromEntries(
      ['/api', '/health'].map((path) => [
        path,
        {
          target: 'http://localhost:5080',
          changeOrigin: true,
          headers: { 'X-Forwarded-Proto': 'https' },
        },
      ]),
    ),
  },
  build: { sourcemap: false },
}))
