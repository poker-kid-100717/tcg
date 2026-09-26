/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 3000,
    // The API runs separately in development (dotnet run, port 5259). Keep the browser's Host header: the API
    // accepts a state-changing request only when its Origin matches the host it was sent to.
    proxy: { '/api': { target: 'http://localhost:5259', changeOrigin: false } },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/setupTests.ts',
  },
});
