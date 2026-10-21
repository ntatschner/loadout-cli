// Studio's page-level layers come from one named scale in tokens.css. A
// hand-picked number is how the mode bar came to cover the open rail, and how
// a rule for an element nothing renders (#nav) went unnoticed.
import { describe, expect, it } from 'vitest';
import { readdirSync, readFileSync } from 'node:fs';

const SHEETS = ['src/styles/components.css', 'src/styles/pages.css', 'src/styles/studio.css'];

/** The layers from the bottom up, as tokens.css and studio.tokens.json document them. */
const ORDER = ['z-page', 'z-rail', 'z-modebar', 'z-focus', 'z-rail-open', 'z-menu', 'z-scrim', 'z-dialog', 'z-toast', 'z-skip', 'z-ambient'];

/** Stacking inside one component, which never reaches another region of the screen. */
const LOCAL: Record<string, number> = {
  '.ls-halo .ring b': 1,
  '.ls-shape .stn': 1,
  '.ls-modes button': 1,
  '.ls-stabs .tab.before::before, .ls-stabs .tab.after::after': 2,
  '.ls-stabs .hint': 3,
  '.ls-load .bar .seg:focus-visible': 1,
  '.tfocus .bpanel .pclose': 2,
  '.ls-split': 2,
};

const strip = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '');
const rules = (css: string) => [...strip(css).matchAll(/([^{}]+)\{([^{}]*)\}/g)].map(([, selector, body]) => ({ selector: selector.trim(), body }));
const sheetRules = () => SHEETS.flatMap((sheet) => rules(readFileSync(sheet, 'utf-8')).map((rule) => ({ ...rule, sheet })));
const declared = (body: string, property: string) => [...body.matchAll(new RegExp(`(?:^|;)\\s*${property}\\s*:\\s*([^;]+)`, 'g'))].map(([, value]) => value.trim());

function tokens(): [string, number][] {
  const root = rules(readFileSync('../design/studio/tokens.css', 'utf-8')).filter((rule) => rule.selector === ':root');
  return root.flatMap((rule) => [...rule.body.matchAll(/--(z-[\w-]+):\s*(-?\d+)\s*;/g)].map(([, name, value]) => [name, Number(value)] as [string, number]));
}

describe('the stacking scale', () => {
  it('names every page-level z-index, and keeps local numbers below the page layer', () => {
    const page = Object.fromEntries(tokens())['z-page'];
    const stray = sheetRules()
      .flatMap((rule) => declared(rule.body, 'z-index').map((value) => ({ ...rule, value })))
      .filter(({ selector, body, value }) => {
        if (/^var\(--z-[\w-]+\)$/.test(value)) {
          return false;
        }
        const local = LOCAL[selector];
        return local === undefined || String(local) !== value || local >= page || /position:\s*fixed/.test(body);
      })
      .map(({ sheet, selector, value }) => `${sheet}: ${selector} { z-index: ${value} }`);

    expect(stray).toEqual([]);
  });

  it('declares the layers in tokens.css in the documented order, each above the last', () => {
    const declaredTokens = tokens();

    expect(declaredTokens.map(([name]) => name)).toEqual(ORDER);
    declaredTokens.slice(1).forEach(([name, value], index) => {
      expect(value, `--${name} sits above --${declaredTokens[index][0]}`).toBeGreaterThan(declaredTokens[index][1]);
    });
  });

  it('matches the layers in studio.tokens.json', () => {
    const json = JSON.parse(readFileSync('../design/studio/studio.tokens.json', 'utf-8'));
    const layers = Object.entries(json.layer as Record<string, { $value: number }>)
      .filter(([name]) => !name.startsWith('$'))
      .map(([name, token]) => [name, token.$value]);

    expect(layers).toEqual(tokens());
  });

  it('lifts the widened rail and the Pages slide-in above the mode bar', () => {
    const all = sheetRules();
    const zOf = (selector: string) => all.filter((rule) => rule.selector === selector).flatMap((rule) => declared(rule.body, 'z-index'));

    expect(zOf('.ls-railnav:hover, .ls-railnav:focus-within')).toEqual(['var(--z-rail-open)']);
    expect(zOf('.ls-slidescrim')).toEqual(['var(--z-rail-open)']);
    expect(zOf('.ls-slide')).toEqual(['var(--z-rail-open)']);
    expect(zOf('.ls-pagestab')).toEqual(['var(--z-rail-open)']);
    expect(zOf('#modebar')).toEqual(['var(--z-modebar)']);
  });
});

describe('the rules', () => {
  it('select only ids that something renders', () => {
    const sources = readdirSync('src', { recursive: true, encoding: 'utf-8' }).filter((file) => /\.(svelte|ts)$/.test(file) && !file.endsWith('.test.ts'));
    const markup = [readFileSync('index.html', 'utf-8'), ...sources.map((file) => readFileSync(`src/${file}`, 'utf-8'))].join('\n');
    const rendered = new Set([...markup.matchAll(/\bid="([\w-]+)"/g)].map(([, id]) => id));
    const selected = new Set(sheetRules().flatMap((rule) => [...rule.selector.matchAll(/#([a-zA-Z][\w-]*)/g)].map(([, id]) => id)));

    expect([...selected].filter((id) => !rendered.has(id))).toEqual([]);
  });
});
