// The pieces of Node the tests read with: a stylesheet as it is written,
// which Vitest's CSS handling would otherwise give back empty, and the names
// of the sources beside it. Declared here rather than by adding @types/node
// for two functions.
declare module 'node:fs' {
  export function readFileSync(path: string, encoding: 'utf-8'): string;
  export function readdirSync(path: string, options: { recursive: true; encoding: 'utf-8' }): string[];
}
