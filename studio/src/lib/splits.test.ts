// Where the dividers sit: what is kept is read back only when it can be
// used, a fraction is held between its limits, and a terminal's floor is
// worked out from its text size.
import { describe, expect, it } from 'vitest';
import { clampSplit, floorOf, readSplit, terminalFloor, tracks } from './splits';

describe('readSplit', () => {
  it('reads back the fractions kept for a split', () => {
    expect(readSplit('[0.3]', 1)).toEqual([0.3]);
    expect(readSplit('[0.6,0.7]', 2)).toEqual([0.6, 0.7]);
    expect(readSplit('[0.2,0.25]', 2, true)).toEqual([0.2, 0.25]);
  });

  it('gives the default for nothing kept, or anything it cannot use', () => {
    for (const text of ['', 'nonsense', '{}', '0.4', '[]', '[0.4,0.5]', '["0.4"]', '[0]', '[1]', '[-0.2]', '[1.5]', '[null]']) {
      expect(readSplit(text, 1), text).toBeNull();
    }
  });

  it('refuses a set that leaves the pane it does not name nothing, only where they share one width', () => {
    expect(readSplit('[0.6,0.5]', 2, true)).toBeNull();
    expect(readSplit('[0.5,0.5]', 2, true)).toBeNull();
    expect(readSplit('[0.6,0.5]', 2)).toEqual([0.6, 0.5]);
  });
});

describe('clampSplit', () => {
  it('holds a fraction between its limits', () => {
    expect(clampSplit(0.05, 0.2, 0.8)).toBe(0.2);
    expect(clampSplit(0.95, 0.2, 0.8)).toBe(0.8);
    expect(clampSplit(0.4, 0.2, 0.8)).toBe(0.4);
  });

  it('settles in the middle where the limits cross', () => {
    expect(clampSplit(0.1, 0.6, 0.4)).toBe(0.5);
  });
});

describe('floors', () => {
  it('works out 40 columns by 8 rows from the text size', () => {
    expect(terminalFloor(13)).toEqual({ width: 336, height: 202 });
    expect(terminalFloor(20).width).toBeGreaterThan(terminalFloor(13).width);
  });

  it('turns a floor into a fraction of the length, never past a half, and nothing where there is no length yet', () => {
    expect(floorOf(250, 1000)).toBe(0.25);
    expect(floorOf(800, 1000)).toBe(0.5);
    expect(floorOf(250, 0)).toBe(0);
    expect(floorOf(250, -10)).toBe(0);
  });

  it('shares a length between two tracks by the fraction alone', () => {
    expect(tracks(0.3)).toBe('minmax(0, 0.3000fr) minmax(0, 0.7000fr)');
  });
});
