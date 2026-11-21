/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// The bench API configures no CORS: in development requests go through the Vite proxy.
const api = process.env.PROFITFOX_BENCH_API ?? 'http://localhost:5240';

// Unit tests run in a zone other than UTC, so every expectation proves the page shows UTC as the bench reports do.
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
    proxy: { '/api': { target: api } },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
