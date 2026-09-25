import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 3000,
    // The API runs separately in development (dotnet run, port 5259).
    proxy: { '/api': 'http://localhost:5259' },
  },
  test: {
    environment: 'jsdom',
    setupFiles: './src/setupTests.js',
  },
});
