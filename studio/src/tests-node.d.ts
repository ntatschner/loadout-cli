// The one piece of Node the tests read with: a stylesheet as it is written,
// which Vitest's CSS handling would otherwise give back empty. Declared here
// rather than by adding @types/node for one function.
declare module 'node:fs' {
  export function readFileSync(path: string, encoding: 'utf-8'): string;
}
