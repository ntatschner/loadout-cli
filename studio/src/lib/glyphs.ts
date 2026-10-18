// The small glyphs the page components draw beside a word, on a 20x20 grid
// with round caps, ported from the design system's bundle.js. A glyph never
// carries meaning alone: the word beside it does.

export const GLYPHS = {
  lock: 'M6 9V6.5a4 4 0 0 1 8 0V9 M5 9h10v8H5z',
  error: 'M6 6 L14 14 M14 6 L6 14',
  warning: 'M10 4 V11.5 M10 15 V15.2',
  info: 'M10 9 V14.5 M10 5.6 V5.8',
  ok: 'M5 10.5 L8.5 14 L15 6.5',
  fix: 'M12.5 3.5a3.5 3.5 0 0 0-3.3 4.6L3.5 13.8l2.7 2.7 5.7-5.7a3.5 3.5 0 0 0 4.6-3.3l-2 2-2-.5-.5-2z',
  chev: 'M7 4 L13 10 L7 16',
  term: 'M3 4h14v12H3z M6 8l3 2-3 2 M10.5 12.5h3.5',
} as const;

export type GlyphName = keyof typeof GLYPHS;

/** Tokens as somebody reads them: 1.2k, 3.4M, 1.1B, with a trailing .0 dropped. */
export function tokens(value: number): string {
  const n = Number(value) || 0;
  const short = (x: number, unit: string) => `${x.toFixed(1).replace(/\.0$/, '')}${unit}`;

  return n >= 1e9 ? short(n / 1e9, 'B') : n >= 1e6 ? short(n / 1e6, 'M') : n >= 1e3 ? short(n / 1e3, 'k') : String(Math.round(n));
}

/** "1 thing", "2 things". */
export function count(n: number, one: string, many = `${one}s`): string {
  return `${n} ${n === 1 ? one : many}`;
}
