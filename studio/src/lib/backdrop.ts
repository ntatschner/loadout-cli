// What is behind the page right now: the plain ground, the field map, or the
// map with its scan. One place decides, from everything that can override
// the setting, and App.svelte writes the answer as data-fieldmap on <html>
// for the CSS to key off. Kept free of the DOM so it can be checked from a
// table.

import type { StudioMode } from './modes';

export type FieldMapShown = 'plain' | 'map' | 'map-scan';

export interface BackdropInputs {
  /** The studio-backdrop setting as kept; anything unknown is plain. */
  setting: string | null | undefined;
  theme: string;
  mode: StudioMode;
  ambient: boolean;
  /** The motion in force, the system's reduced motion included: motionNow(). */
  motion: 'full' | 'reduced' | 'none';
  /** How many things wait on you: the ModeBar's count. */
  waiting: number;
  hidden: boolean;
  /** Whether a terminal has the keyboard. */
  terminalFocused: boolean;
}

/** In order: high contrast, Pure Focus and Ambient keep the plain ground; the scan runs only under full motion while nothing needs you. */
export function resolveBackdrop(o: BackdropInputs): FieldMapShown {
  if (o.theme === 'hc-dark' || o.theme === 'hc-light' || o.mode === 'pure' || o.ambient) {
    return 'plain';
  }

  if (o.setting === 'map') {
    return 'map';
  }

  if (o.setting !== 'map-animated') {
    return 'plain';
  }

  return o.motion === 'full' && o.waiting === 0 && !o.hidden && !o.terminalFocused ? 'map-scan' : 'map';
}
