// The field map behind every page: contour lines over a seeded value noise,
// a grid and its map references. Only the geometry; FieldMap.svelte draws it
// and CSS decides whether it shows. Ported from the design system's
// bundle.js, and kept free of the DOM so the same seed can be checked to give
// the same map.

export interface FieldMapOptions {
  /** The noise's seed. The same seed gives the same map on every visit and every machine. */
  seed?: number;
  /** The marching-squares cell, in pixels. */
  cell?: number;
  /** How far apart the hills are, in pixels per unit of noise. */
  scale?: number;
  /** How many contour levels; every fifth is an index contour. */
  levels?: number;
  /** The grid square, in pixels. */
  square?: number;
}

export interface MapReference {
  x: number;
  y: number;
  text: string;
  anchor: 'start' | 'middle';
}

export interface FieldMapGeometry {
  /** Path data for the ordinary contours. */
  minor: string;
  /** Path data for every fifth contour, drawn heavier. */
  index: string;
  /** Where the grid's vertical lines fall. */
  columns: number[];
  /** Where the grid's horizontal lines fall. */
  rows: number[];
  /** A, B… along the foot and 01, 02… down the left edge. */
  references: MapReference[];
}

export const FIELD_MAP_SEED = 1789;

/** Survey letters, without I, which reads as 1. */
const LETTERS = 'ABCDEFGHJKLMNOPQRSTUVWXYZ';

/** A small xorshift generator, so the noise is the same for the same seed. */
function rng(seed: number): () => number {
  let x = seed >>> 0 || 1;

  return () => {
    x ^= x << 13;
    x >>>= 0;
    x ^= x >>> 17;
    x ^= x << 5;
    x >>>= 0;

    return x / 4294967296;
  };
}

/** Value noise in three octaves. */
function noise(seed: number): (x: number, y: number) => number {
  const r = rng(seed);
  const size = 256;
  const perm: number[] = [];
  const val: number[] = [];

  for (let i = 0; i < size; i++) {
    perm[i] = i;
    val[i] = r();
  }

  for (let j = size - 1; j > 0; j--) {
    const k = Math.floor(r() * (j + 1));
    [perm[j], perm[k]] = [perm[k], perm[j]];
  }

  const at = (x: number, y: number) => val[perm[(perm[x & 255] + y) & 255]];
  const smooth = (t: number) => t * t * (3 - 2 * t);
  const value = (x: number, y: number) => {
    const x0 = Math.floor(x);
    const y0 = Math.floor(y);
    const fx = smooth(x - x0);
    const fy = smooth(y - y0);
    const a = at(x0, y0);
    const b = at(x0 + 1, y0);
    const c = at(x0, y0 + 1);
    const d = at(x0 + 1, y0 + 1);

    return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
  };

  return (x, y) => value(x, y) * 0.58 + value(x * 2.1 + 17, y * 2.1 + 9) * 0.28 + value(x * 4.3 + 41, y * 4.3 + 3) * 0.14;
}

type Point = [number, number];

/** The segments one marching-squares cell crosses, by which of its corners are above the level. */
function segments(code: number, top: Point, right: Point, bottom: Point, left: Point): [Point, Point][] {
  switch (code) {
    case 1: return [[left, bottom]];
    case 2: return [[bottom, right]];
    case 3: return [[left, right]];
    case 4: return [[top, right]];
    case 5: return [[left, top], [bottom, right]];
    case 6: return [[top, bottom]];
    case 7: return [[left, top]];
    case 8: return [[left, top]];
    case 9: return [[top, bottom]];
    case 10: return [[left, bottom], [top, right]];
    case 11: return [[top, right]];
    case 12: return [[left, right]];
    case 13: return [[bottom, right]];
    case 14: return [[left, bottom]];
    default: return [];
  }
}

/** The contours, by marching squares over the noise: one path for the ordinary lines and one for every fifth. */
function contours(width: number, height: number, o: FieldMapOptions): { minor: string; index: string } {
  const cell = o.cell ?? 12;
  const scale = o.scale ?? 260;
  const n = noise(o.seed ?? FIELD_MAP_SEED);
  const cols = Math.ceil(width / cell) + 1;
  const rows = Math.ceil(height / cell) + 1;
  const z: number[][] = [];

  for (let y = 0; y < rows; y++) {
    z[y] = [];
    for (let x = 0; x < cols; x++) {
      z[y][x] = n((x * cell) / scale, (y * cell) / scale);
    }
  }

  const levels = o.levels ?? 22;
  const lo = 0.12;
  const hi = 0.88;
  const minor: string[] = [];
  const index: string[] = [];

  for (let level = 1; level < levels; level++) {
    const t = lo + ((hi - lo) * level) / levels;
    const into = level % 5 === 0 ? index : minor;
    const lerp = (p: number, q: number) => (t - p) / (q - p);

    for (let y = 0; y < rows - 1; y++) {
      for (let x = 0; x < cols - 1; x++) {
        const a = z[y][x];
        const b = z[y][x + 1];
        const c = z[y + 1][x + 1];
        const e = z[y + 1][x];
        const code = (a > t ? 8 : 0) | (b > t ? 4 : 0) | (c > t ? 2 : 0) | (e > t ? 1 : 0);

        if (code === 0 || code === 15) {
          continue;
        }

        const X = x * cell;
        const Y = y * cell;
        const top: Point = [X + cell * lerp(a, b), Y];
        const right: Point = [X + cell, Y + cell * lerp(b, c)];
        const bottom: Point = [X + cell * lerp(e, c), Y + cell];
        const left: Point = [X, Y + cell * lerp(a, e)];

        for (const [from, to] of segments(code, top, right, bottom, left)) {
          into.push(`M${from[0].toFixed(1)} ${from[1].toFixed(1)}L${to[0].toFixed(1)} ${to[1].toFixed(1)}`);
        }
      }
    }
  }

  return { minor: minor.join(''), index: index.join('') };
}

/** The whole map for a page of this size. */
export function fieldMap(width: number, height: number, options: FieldMapOptions = {}): FieldMapGeometry {
  const square = options.square ?? 200;
  const columns: number[] = [];
  const rows: number[] = [];
  const references: MapReference[] = [];

  for (let x = square; x < width; x += square) {
    columns.push(x);
  }

  for (let y = square; y < height; y += square) {
    rows.push(y);
  }

  for (let i = 0; i * square < width; i++) {
    references.push({ x: i * square + square / 2, y: height - 14, text: LETTERS[i % LETTERS.length], anchor: 'middle' });
  }

  for (let j = 0; j * square < height; j++) {
    references.push({ x: 12, y: j * square + square / 2 + 4, text: String(j + 1).padStart(2, '0'), anchor: 'start' });
  }

  return { ...contours(width, height, options), columns, rows, references };
}
