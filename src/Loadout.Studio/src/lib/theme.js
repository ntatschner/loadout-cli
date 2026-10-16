import tokens from './tokens.json';

/**
 * Writes the tokens for a theme, an accent and a density onto the page as CSS
 * custom properties. Components read only these properties, so the look is
 * this file's input and nothing else.
 *
 * @param {{ theme?: string, accent?: string, density?: string }} choice
 */
export function applyTokens(choice = {}) {
  const theme = resolveTheme(choice.theme ?? 'system');
  const accent = tokens.accents[choice.accent ?? 'bay'] ?? tokens.accents.bay;
  const density = tokens.density[choice.density ?? 'comfortable'] ?? tokens.density.comfortable;
  const style = document.documentElement.style;

  const set = (prefix, group) => {
    for (const [name, value] of Object.entries(group)) {
      style.setProperty(`--${prefix}${name}`, value);
    }
  };

  set('colour-', tokens.themes[theme]);
  set('colour-', accent[theme]);
  set('', density);
  set('', tokens.type);
  set('radius-', tokens.radius);
  set('shadow-', tokens.shadow);
  set('motion-', tokens.motion);
  set('focus-', tokens.focus);

  document.documentElement.dataset.theme = theme;
  document.documentElement.style.colorScheme = theme === 'light' ? 'light' : 'dark';
}

/** The theme the colours come from, with "system" answered by the browser. */
function resolveTheme(asked) {
  if (asked in tokens.themes) {
    return asked;
  }

  if (window.matchMedia('(prefers-contrast: more)').matches) {
    return 'contrast';
  }

  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

/** Applies the tokens now, and again when the system's preference changes. */
export function followSystem() {
  applyTokens();

  for (const query of ['(prefers-color-scheme: dark)', '(prefers-contrast: more)']) {
    window.matchMedia(query).addEventListener('change', () => applyTokens());
  }
}
