import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

// Served by the daemon under /studio/. Everything the page needs is in the
// bundle; nothing is fetched from anywhere else at runtime.
export default defineConfig({
  base: '/studio/',
  plugins: [svelte()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    assetsInlineLimit: 0,
    sourcemap: false,
  },
  server: {
    // 'npm run dev' against a daemon on its default port, for working on
    // the page with hot reload.
    proxy: {
      '/api': { target: 'http://127.0.0.1:47611', ws: true },
    },
  },
});
