// Text stays readable on the field map. Each layer's level is read from
// components.css and each colour from studio.tokens.json, so moving either
// fails here. The worst pixel is where an index contour, one of its edges, a
// grid line and the scan all meet, or where a map reference sits on a grid
// line: each is composited over the ground in sRGB, and every text colour
// must stay 4.5:1 or better on it, in Dark and Light and every accent.
import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';

type Theme = 'dark' | 'light';
type Rgb = [number, number, number];

const tokens = JSON.parse(readFileSync('../design/studio/studio.tokens.json', 'utf-8'));
const css = readFileSync('src/styles/components.css', 'utf-8');

const ACCENTS = ['cyan', 'ice', 'magenta', 'violet'] as const;
const TEXT = ['ink', 'ink-soft', 'muted', 'needs-text', 'bad-text', 'good'] as const;

const hex = (value: string): Rgb => {
  const digits = value.replace('#', '');
  const full = digits.length === 3 ? [...digits].map((d) => d + d).join('') : digits;
  return [0, 2, 4].map((at) => parseInt(full.slice(at, at + 2), 16)) as Rgb;
};

const themed = (token: { $extensions: { 'studio.themes': Record<string, string> } }, theme: Theme) => hex(token.$extensions['studio.themes'][theme]);

function colour(name: string, theme: Theme, accent: string): Rgb {
  if (name === '#fff') return [255, 255, 255];
  if (name === '#000') return [0, 0, 0];
  if (name === 'accent') return themed(tokens.accent[accent].fill, theme);
  if (name === 'accent-text') return themed(tokens.accent[accent].text, theme);
  return themed(tokens.color[name], theme);
}

/** The --fm-* levels for a theme, as components.css sets them: colour and alpha. */
function levels(theme: Theme): Record<string, { of: string; alpha: number }> {
  const block = theme === 'dark'
    ? css.match(/\n\.ls-fieldmap \{[^}]*\}/)![0]
    : css.match(/\[data-theme="light"\] \.ls-fieldmap \{[^}]*\}/)![0];

  return Object.fromEntries(
    [...block.matchAll(/--fm-([\w-]+):\s*color-mix\(in srgb,\s*(var\(--([\w-]+)\)|#fff|#000)\s+([\d.]+)%,\s*transparent\)/g)]
      .map(([, layer, raw, token, percent]) => [layer, { of: token ?? raw, alpha: Number(percent) / 100 }]),
  );
}

const over = (bottom: Rgb, top: Rgb, alpha: number): Rgb => bottom.map((b, i) => top[i] * alpha + b * (1 - alpha)) as Rgb;

const luminance = (rgb: Rgb) => {
  const [r, g, b] = rgb.map((c) => {
    const s = c / 255;
    return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};

const ratio = (a: Rgb, b: Rgb) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
};

/**
 * The places a text pixel can sit, each a stack of layers from the bottom up.
 * Each contour stack comes with the scan over it and without: in Dark the
 * scan lightens, which is worst for light text, and in Light it is a glint
 * of white, so the worst there is the contour with no scan crossing it.
 */
const CONTOURS: Record<string, string[]> = {
  'an index contour, its light edge and a grid line': ['grid', 'hi', 'index'],
  'an index contour, its dark edge and a grid line': ['grid', 'lo', 'index'],
  'a contour, its light edge and a grid line': ['grid', 'hi', 'line'],
  'a contour, its dark edge and a grid line': ['grid', 'lo', 'line'],
};
const STACKS: Record<string, string[]> = {
  ...CONTOURS,
  ...Object.fromEntries(Object.entries(CONTOURS).map(([where, stack]) => [`${where}, with the scan`, [...stack, 'scan']])),
  'a reference and a grid line': ['grid', 'ref'],
};

interface Worst { ratio: number; text: string; accent: string; where: string }

function worst(theme: Theme): Worst {
  const level = levels(theme);
  let found: Worst = { ratio: Infinity, text: '', accent: '', where: '' };

  for (const accent of ACCENTS) {
    for (const [where, stack] of Object.entries(STACKS)) {
      const ground = stack.reduce((under, layer) => over(under, colour(level[layer].of, theme, accent), level[layer].alpha), colour('ground', theme, accent));

      for (const text of [...TEXT, 'accent-text']) {
        const r = ratio(colour(text, theme, accent), ground);
        if (r < found.ratio) found = { ratio: r, text, accent, where };
      }
    }
  }

  return found;
}

describe('text on the field map', () => {
  it('reads every level it composites', () => {
    for (const theme of ['dark', 'light'] as Theme[]) {
      expect(Object.keys(levels(theme)).sort(), theme).toEqual(['grid', 'hi', 'index', 'line', 'lo', 'ref', 'scan']);
    }
  });

  it.each(['dark', 'light'] as Theme[])('stays 4.5:1 or better on the worst pixel in %s', (theme) => {
    const found = worst(theme);

    expect(found.ratio, `${found.text} with ${found.accent}, over ${found.where}`).toBeGreaterThanOrEqual(4.5);
  });

  it('finds the lowest pixels BACKGROUND.md measured', () => {
    const dark = worst('dark');
    const light = worst('light');

    expect([dark.text, dark.accent, dark.ratio.toFixed(2)]).toEqual(['muted', 'ice', '5.60']);
    expect([light.text, light.accent, light.where, light.ratio.toFixed(2)]).toEqual(['good', 'cyan', 'a reference and a grid line', '4.52']);
  });
});
