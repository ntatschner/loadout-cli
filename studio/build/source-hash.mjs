// What a Studio build is made from, as one hash. StudioBuildTests recomputes
// the same thing in C#, so keep the two in step: the files, the order, and the
// one normalisation (carriage returns removed from text) are the contract.
import { createHash } from 'node:crypto';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';

/** Files and folders, relative to the repository root, that a build reads. */
export const INPUTS = [
  'studio/index.html',
  'studio/package-lock.json',
  'studio/svelte.config.js',
  'studio/tsconfig.json',
  'studio/vite.config.ts',
  'studio/build/source-hash.mjs',
  'studio/public',
  'studio/src',
  'design/studio/tokens.css',
  'design/studio/fonts',
];

const BINARY = ['.woff2', '.png', '.ico'];

function walk(root, path, found) {
  const full = join(root, path);

  if (statSync(full).isDirectory()) {
    for (const name of readdirSync(full)) {
      walk(root, join(path, name), found);
    }

    return;
  }

  const posix = path.split(sep).join('/');

  // Tests do not change what is built, and a build forced by every edit to
  // one would be a stale-build failure nobody could explain.
  if (!/\.test\.ts$/.test(posix)) {
    found.push(posix);
  }
}

/** The hash, given the repository root. */
export function sourceHash(root) {
  const files = [];

  for (const input of INPUTS) {
    walk(root, input, files);
  }

  files.sort();

  const hash = createHash('sha256');

  for (const file of files) {
    let bytes = readFileSync(join(root, file));

    if (!BINARY.some((ext) => file.endsWith(ext))) {
      bytes = Buffer.from(bytes.toString('utf8').replace(/\r/g, ''), 'utf8');
    }

    hash.update(file + '\n');
    hash.update(bytes);
    hash.update('\n');
  }

  return hash.digest('hex');
}
