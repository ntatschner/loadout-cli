// What is behind the page, from the setting and everything that overrides
// it, row by row as BACKGROUND.md's Decisions table has it; then the field
// map's geometry, the setting as kept on <html>, and the setting read from
// the preferences.
import { afterEach, describe, expect, it } from 'vitest';
import { resolveBackdrop, type BackdropInputs } from './backdrop';
import { fieldMap } from './fieldmap';
import { setBackdrop } from './studio';
import { Studio } from './state.svelte';
import type { Preferences } from './api';

/** Everything at its most permissive: the animated map, full motion, nothing waiting, the tab in front. */
const scanning: BackdropInputs = {
  setting: 'map-animated',
  theme: 'dark',
  mode: 'default',
  ambient: false,
  motion: 'full',
  waiting: 0,
  hidden: false,
  terminalFocused: false,
};

describe('resolveBackdrop', () => {
  it.each([
    ['either high-contrast theme', { theme: 'hc-dark' }, 'plain'],
    ['high contrast light', { theme: 'hc-light' }, 'plain'],
    ['Pure Focus', { mode: 'pure' }, 'plain'],
    ['Ambient', { ambient: true }, 'plain'],
    ['the setting plain', { setting: 'plain' }, 'plain'],
    ['the setting unset', { setting: null }, 'plain'],
    ['a setting it does not know', { setting: 'map-scan' }, 'plain'],
    ['the setting map', { setting: 'map' }, 'map'],
    ['map-animated with nothing against it', {}, 'map-scan'],
  ] as [string, Partial<BackdropInputs>, string][])('gives %s → %s', (_, over, shown) => {
    expect(resolveBackdrop({ ...scanning, ...over })).toBe(shown);
  });

  it.each([
    ['motion reduced', { motion: 'reduced' }],
    ['motion none', { motion: 'none' }],
    ['one thing waiting', { waiting: 1 }],
    ['a hidden tab', { hidden: true }],
    ['a focused terminal', { terminalFocused: true }],
  ] as [string, Partial<BackdropInputs>][])('stills the scan for %s alone, keeping the map', (_, over) => {
    expect(resolveBackdrop({ ...scanning, ...over })).toBe('map');
  });

  it('keeps the map in Light and in the other modes', () => {
    for (const mode of ['terminals', 'teams', 'minimal'] as const) {
      expect(resolveBackdrop({ ...scanning, theme: 'light', mode })).toBe('map-scan');
    }
  });
});

describe('the field map', () => {
  it('is the same map for the same seed and size', () => {
    expect(fieldMap(640, 400, { seed: 1789 })).toEqual(fieldMap(640, 400, { seed: 1789 }));
    expect(fieldMap(640, 400)).toEqual(fieldMap(640, 400, { seed: 1789 }));
  });

  it('is a different map for a different seed', () => {
    const one = fieldMap(640, 400, { seed: 1789 });
    const other = fieldMap(640, 400, { seed: 42 });

    expect(other.minor).not.toBe(one.minor);
    expect(other.index).not.toBe(one.index);
  });

  it('puts every fifth level, and only those, in the index path', () => {
    // Levels 1 to n-1 are drawn, level L at height L/n of the way up. The
    // fifth of ten and the first of two are both at mid height, so ten's
    // index path is exactly the one line two levels draw, which is minor.
    const ten = fieldMap(640, 400, { levels: 10 });
    const two = fieldMap(640, 400, { levels: 2 });

    expect(two.minor).not.toBe('');
    expect(two.index).toBe('');
    expect(ten.index).toBe(two.minor);
    expect(ten.minor).not.toContain(two.minor);
  });

  it('runs its references A, B… along the foot and 01, 02… down the left', () => {
    const { references } = fieldMap(640, 450);
    const foot = references.filter((ref) => ref.anchor === 'middle').map((ref) => ref.text);
    const side = references.filter((ref) => ref.anchor === 'start').map((ref) => ref.text);

    expect(foot).toEqual(['A', 'B', 'C', 'D']);
    expect(side).toEqual(['01', '02', '03']);
    expect(references.filter((ref) => ref.anchor === 'start').every((ref) => ref.x === 12)).toBe(true);
  });

  it('draws a grid line every 200px, inside the page', () => {
    const map = fieldMap(640, 450);

    expect(map.columns).toEqual([200, 400, 600]);
    expect(map.rows).toEqual([200, 400]);
  });
});

describe('the backdrop setting', () => {
  afterEach(() => document.documentElement.removeAttribute('data-backdrop'));

  it.each(['plain', 'map', 'map-animated'])('keeps %s on <html>', (value) => {
    setBackdrop(value);

    expect(document.documentElement.getAttribute('data-backdrop')).toBe(value);
  });

  it('keeps plain for anything it does not know', () => {
    setBackdrop('map-scan');

    expect(document.documentElement.getAttribute('data-backdrop')).toBe('plain');
  });

  it('is set from the preferences, and plain where they have none', () => {
    const studio = new Studio();

    studio.prefs = { backdrop: 'map' } as Preferences;
    studio.applyPrefs();
    expect(document.documentElement.getAttribute('data-backdrop')).toBe('map');

    studio.prefs = {} as Preferences;
    studio.applyPrefs();
    expect(document.documentElement.getAttribute('data-backdrop')).toBe('plain');
  });
});
