// Studio's foundations: theme, accent, density and motion, set as attributes
// on <html> so every token in tokens.css rebinds at once. Ported from the
// design system's bundle.js.

export type Theme = 'dark' | 'light' | 'hc-dark' | 'hc-light';
export type Accent = 'cyan' | 'ice' | 'magenta' | 'violet';
export type Density = 'compact' | 'comfortable' | 'roomy';
export type Motion = 'full' | 'reduced' | 'none';

export const THEMES: Theme[] = ['dark', 'light', 'hc-dark', 'hc-light'];
export const ACCENTS: Accent[] = ['cyan', 'ice', 'magenta', 'violet'];
export const DENSITIES: Density[] = ['compact', 'comfortable', 'roomy'];
export const MOTIONS: Motion[] = ['full', 'reduced', 'none'];
export type Backdrop = 'plain' | 'map' | 'map-animated';
export const BACKDROPS: Backdrop[] = ['plain', 'map', 'map-animated'];

export const THEME_NAMES: Record<Theme, string> = {
  dark: 'Dark',
  light: 'Light',
  'hc-dark': 'High contrast dark',
  'hc-light': 'High contrast light',
};

function pick<T extends string>(list: readonly T[], value: string | null | undefined, fallback: T): T {
  return list.includes(value as T) ? (value as T) : fallback;
}

function root(el?: Element): Element {
  return el ?? document.documentElement;
}

/** Sets data-theme on <html>, or on `el`. Unknown values fall back to dark. */
export function setTheme(theme: string, el?: Element): void {
  root(el).setAttribute('data-theme', pick(THEMES, theme, 'dark'));
}

/** Sets data-accent, which rebinds accent, accent-text and on-accent. */
export function setAccent(accent: string, el?: Element): void {
  root(el).setAttribute('data-accent', pick(ACCENTS, accent, 'cyan'));
}

/** Sets data-density, which multiplies the spacing scale. */
export function setDensity(density: string, el?: Element): void {
  root(el).setAttribute('data-density', pick(DENSITIES, density, 'comfortable'));
}

/** Sets data-motion. The operating system's reduced motion still wins: see motion(). */
export function setMotion(level: string, el?: Element): void {
  root(el).setAttribute('data-motion', pick(MOTIONS, level, 'full'));
}

/**
 * Sets data-backdrop, the studio-backdrop setting as kept. Unknown values
 * fall back to plain. What shows is resolveBackdrop()'s, from this and what
 * overrides it.
 */
export function setBackdrop(backdrop: string, el?: Element): void {
  root(el).setAttribute('data-backdrop', pick(BACKDROPS, backdrop, 'plain'));
}

function matches(query: string): boolean {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function' && window.matchMedia(query).matches;
}

/** The motion level in force: the page's setting, lowered to reduced when the system asks for less. */
export function motion(el?: Element): Motion {
  const set = pick(MOTIONS, root(el).getAttribute('data-motion'), 'full');

  return matches('(prefers-reduced-motion: reduce)') && set === 'full' ? 'reduced' : set;
}

/** A first theme for somebody who has not picked one, from the system's colour scheme and contrast. */
export function preferredTheme(): Theme {
  const light = matches('(prefers-color-scheme: light)');
  const more = matches('(prefers-contrast: more)');

  return more ? (light ? 'hc-light' : 'hc-dark') : light ? 'light' : 'dark';
}

/** Whether motion that loops or travels may run now. */
export function fullMotion(): boolean {
  return motion() === 'full';
}
