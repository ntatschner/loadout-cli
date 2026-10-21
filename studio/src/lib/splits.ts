// Where the dividers between Studio's panes sit. Each split keeps its own
// fractions in this browser, beside studio-deck-layout, so a split dragged
// here stays where it was put and the daemon knows nothing of it. Fractions
// rather than pixels, so a split keeps its proportions as the window changes.

/** The four splits Studio has, each with its own key. The deck keeps one set per layout, since two panes and four are different choices. */
export const SPLIT_KEYS = {
  deck2: 'studio-split-deck-2',
  deck4: 'studio-split-deck-4',
  beside: 'studio-split-beside',
  desk: 'studio-split-desk',
} as const;

/**
 * Raised on the document when a divider is let go, or its key released: a
 * terminal holds back telling its session a new size while a divider moves,
 * and this says to tell it now.
 */
export const SPLIT_SETTLED = 'ls-split-settled';

/** How small a pane holding a terminal may go: an agent squeezed below this can't draw. */
export const TERMINAL_FLOOR = { columns: 40, rows: 8 } as const;

/**
 * The pixels a terminal needs for TERMINAL_FLOOR at this font size: a
 * monospace cell is about 0.6 of the size wide, and the terminal draws its
 * lines 1.4 high. The extra is the bar above the screen and its padding.
 */
export function terminalFloor(fontSize = 13): { width: number; height: number } {
  return {
    width: Math.ceil(TERMINAL_FLOOR.columns * fontSize * 0.6 + 24),
    height: Math.ceil(TERMINAL_FLOOR.rows * fontSize * 1.4 + 56),
  };
}

/**
 * The fractions kept for a split, or null where none are kept or what is kept
 * can't be used: not a list, the wrong count, a value that isn't between 0
 * and 1, or, where they share one length with a pane they don't name, a
 * set that leaves that pane nothing. Null means the split's own default.
 */
export function readSplit(text: string, count: number, sharing = false): number[] | null {
  let read: unknown;

  try {
    read = JSON.parse(text);
  } catch {
    return null;
  }

  if (!Array.isArray(read) || read.length !== count || !read.every((one) => typeof one === 'number' && Number.isFinite(one) && one > 0 && one < 1)) {
    return null;
  }

  if (sharing && (read as number[]).reduce((sum, one) => sum + one, 0) >= 1) {
    return null;
  }

  return read as number[];
}

/** A fraction held between its limits; where the limits cross, as in a window too small for both floors, the middle of them. */
export function clampSplit(value: number, min: number, max: number): number {
  if (min > max) {
    return (min + max) / 2;
  }

  return Math.min(max, Math.max(min, value));
}

/** A floor in pixels as a fraction of the length shared, kept below a half so two floors always leave a place to stand. */
export function floorOf(pixels: number, length: number): number {
  return length > 0 ? Math.min(0.5, pixels / length) : 0;
}

/** Two tracks sharing a length by a fraction, as CSS: each can shrink to nothing, so the fraction alone decides. */
export function tracks(first: number): string {
  return `minmax(0, ${first.toFixed(4)}fr) minmax(0, ${(1 - first).toFixed(4)}fr)`;
}
