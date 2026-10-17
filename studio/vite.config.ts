import { defineConfig } from 'vitest/config';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { svelteTesting } from '@testing-library/svelte/vite';
import { fileURLToPath } from 'node:url';
import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { sourceHash } from './build/source-hash.mjs';

const here = fileURLToPath(new URL('.', import.meta.url));
const out = join(here, '..', 'src', 'Loadout.Core', 'Teams', 'Daemon', 'Studio');

// Served by the daemon under /studio/, from files committed into Loadout.Core
// so that building Loadout never needs Node. Everything the page needs is in
// the bundle: nothing is fetched from anywhere else at runtime, and nothing is
// emitted inline, because Studio's content security policy allows neither.
export default defineConfig({
  base: '/studio/',
  plugins: [
    svelte(),
    svelteTesting(),
    {
      // The hash of what this build was made from, beside what it made, for
      // StudioBuildTests to recompute: a source changed without a rebuild is
      // a committed build that no longer matches it.
      name: 'loadout-studio-source-hash',
      apply: 'build',
      closeBundle() {
        writeFileSync(join(out, 'studio-build.json'), JSON.stringify({ source: sourceHash(join(here, '..')) }, null, 2) + '\n');
      },
    },
  ],
  build: {
    outDir: out,
    emptyOutDir: true,
    assetsInlineLimit: 0,
    sourcemap: false,
    modulePreload: { polyfill: false },
  },
  // The behaviour tests: each component driven in a document without
  // layout, by keyboard and by pointer, against what its README promises.
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.ts'],
  },
  server: {
    // 'npm run dev' against a daemon on its default port, for working on the
    // page with hot reload. The daemon checks where a change came from, so
    // changes made from the dev server are refused; reading works.
    proxy: {
      '/api': { target: 'http://127.0.0.1:47611', ws: true },
    },
  },
});
