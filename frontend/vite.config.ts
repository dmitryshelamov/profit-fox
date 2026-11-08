/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// The API configures no CORS: in development requests go through the Vite proxy, in production through Caddy.
const api = process.env.PROFITFOX_API ?? 'http://localhost:5242';

// Unit tests run in a zone other than UTC, so every expectation proves the screen shows the local time. Moscow keeps
// UTC+3 all year round; the test workers inherit the zone.
if (process.env.VITEST) {
  process.env.TZ = 'Europe/Moscow';
}

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': new URL('./src', import.meta.url).pathname },
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: { '/api': { target: api, ws: true } },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
