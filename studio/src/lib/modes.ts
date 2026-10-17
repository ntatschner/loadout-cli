// Studio's modes: one switch that re-lays out the whole app around what you
// are doing, kept in this browser. And morph(), which runs a change of
// layout as one animated step: a view transition where the browser has one,
// a rise-in where it does not, a fade under reduced motion, and nothing at
// all under none. Ported from the design system's bundle.js.

import type { GateShown } from './teams';

export type StudioMode = 'default' | 'terminals' | 'teams' | 'minimal' | 'pure';

export interface ModeInfo {
  key: StudioMode;
  name: string;
  desc: string;
  icon: string;
}

export const MODES: ModeInfo[] = [
  { key: 'default', name: 'Default', desc: 'Studio as it is: Home, Sessions, Teams and the rest', icon: 'M4 10a6 6 0 1 0 12 0a6 6 0 1 0-12 0 M9 10a1 1 0 1 0 2 0a1 1 0 1 0-2 0' },
  { key: 'terminals', name: 'Terminals', desc: 'Terminal Focus: sessions as tabs and panes, the terminal as big as it goes', icon: 'M3 4.5h14v11H3z M6 8.2l2.4 1.8L6 11.8 M10.5 12.2h3.5' },
  { key: 'teams', name: 'Teams', desc: 'Team Focus: every live run as a line on one board, each team a click away', icon: 'M2.5 6.5h15 M7 6.5v7h4.5 M13 6.5v3.5h4.5' },
  { key: 'minimal', name: 'Minimal', desc: 'One quiet column of what is live; open one and it is all there is', icon: 'M5 10h7.5 M15.5 10h.01' },
  { key: 'pure', name: 'Pure', desc: 'Pure Focus: list, view and actions in three panes, every key shown, effects off', icon: 'M3 4h14v12H3z M8 4v12 M13 4v12' },
];

export const isMode = (value: string | null | undefined): value is StudioMode => MODES.some((mode) => mode.key === value);

/** The mode after this one, for Ctrl+Shift+M. */
export function nextMode(current: StudioMode): StudioMode {
  const at = MODES.findIndex((mode) => mode.key === current);

  return MODES[(at + 1) % MODES.length].key;
}

/** How much moves: Studio's own setting, with the system's reduced motion always winning. */
export function motionNow(root: HTMLElement = document.documentElement): 'full' | 'reduced' | 'none' {
  const set = root.getAttribute('data-motion');

  if (set === 'none') {
    return 'none';
  }

  if (set === 'reduced' || (typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches)) {
    return 'reduced';
  }

  return 'full';
}

interface Transitioning {
  startViewTransition?: (update: () => void) => { finished: Promise<unknown>; ready?: Promise<unknown> };
}

/**
 * Runs a change of layout as one animated step.
 *
 * Under no motion the change simply happens, and no view transition is
 * started at all. Under reduced motion a view transition, where there is
 * one, is a 180ms fade. Where the browser has none, the page rises in, or
 * fades under reduced motion.
 */
export function morph(apply: () => void, o: { noVT?: boolean; target?: HTMLElement | null } = {}): void {
  const motion = motionNow();
  const root = document.documentElement;

  if (motion === 'none') {
    apply();

    return;
  }

  const doc = document as unknown as Transitioning;

  if (typeof doc.startViewTransition === 'function' && !o.noVT) {
    if (motion !== 'full') {
      root.setAttribute('data-vt', 'fade');
    }

    const done = () => root.removeAttribute('data-vt');
    const transition = doc.startViewTransition(() => apply());
    // A transition the browser cannot capture is skipped, and the change
    // still happens: that is not an error worth reporting.
    transition.ready?.catch(() => undefined);
    transition.finished.then(done, done);

    return;
  }

  apply();

  const target = o.target ?? document.getElementById('main');

  if (target) {
    target.classList.remove('ls-enter', 'ls-enter-fade');
    // Read a layout property so the animation starts again rather than continuing.
    void target.offsetWidth;
    target.classList.add(motion === 'full' ? 'ls-enter' : 'ls-enter-fade');
  }
}

/** Something waiting on you, as the ModeBar counts it: a session or a team run. */
export interface Waiting {
  kind: 'session' | 'run';
  id: string;
  label: string;
  /** When it asked, for putting the oldest first. */
  at: number;
}

/** A session as Terminal Focus's tabs show it. */
export interface DeckSession {
  id: string;
  title: string;
  word: string;
  where?: string | null;
  agent?: string | null;
  unread?: boolean;
}

export type DeckLayout = 1 | 2 | 4;

/** Which sessions a layout shows: the picked one first, then the others in tab order until the panes are full. */
export function shownPanes(order: string[], current: string | null, layout: DeckLayout): string[] {
  const out = current && order.includes(current) ? [current] : [];

  for (const id of order) {
    if (out.length >= layout) {
      break;
    }

    if (!out.includes(id)) {
      out.push(id);
    }
  }

  return out;
}

/**
 * Terminal Focus's own keys: Ctrl+Alt+1–9 picks a session by its tab, and
 * Ctrl+Alt+arrows move between the panes shown. Null where the key is not one of them.
 */
export function deckKey(event: Pick<KeyboardEvent, 'ctrlKey' | 'altKey' | 'key' | 'code'>, order: string[], shown: string[], current: string | null): string | null {
  if (!(event.ctrlKey && event.altKey)) {
    return null;
  }

  // Ctrl+Alt is AltGr on some layouts, where the key reads as another
  // character, so the digit is read from the physical key too.
  const digit = /^Digit([1-9])$/.exec(event.code ?? '')?.[1] ?? (/^[1-9]$/.test(event.key) ? event.key : null);

  if (digit) {
    return order[Number(digit) - 1] ?? null;
  }

  const step = ({ ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 } as Record<string, number>)[event.key];

  if (!step || !shown.length) {
    return null;
  }

  const at = Math.max(0, shown.indexOf(current ?? ''));

  return shown[(at + step + shown.length) % shown.length];
}

/** What a deck reaches in one of its panes: the keyboard, and Find. */
export interface PaneHandle {
  focus: () => void;
  find: (term: string, backwards?: boolean) => boolean;
  clearFind: () => void;
}

/** A run as Team Focus's board shows it. */
export interface BoardRun {
  id: string;
  goal: string;
  team: string;
  project?: string | null;
  word: string;
  round?: number;
  rounds?: { word: string; nodes?: [string, string][] }[];
  spent?: number;
  cap?: number | null;
  projected?: number | null;
  gate?: GateShown | null;
}

/** A team along the top of the board. */
export interface BoardTeam {
  name: string;
  word: string;
  fact?: string;
}

/** Something live, as Minimal's one column lists it. */
export interface QuietItem {
  id: string;
  kind: 'session' | 'team run';
  title: string;
  word: string;
  where?: string | null;
  /** What it asks, said under it where it needs you. */
  ask?: string;
}

/** Ctrl+Shift+M, which cycles the modes from anywhere, a terminal included. */
export function isModeKey(event: Pick<KeyboardEvent, 'ctrlKey' | 'shiftKey' | 'altKey' | 'metaKey' | 'key' | 'code'>): boolean {
  return event.ctrlKey && event.shiftKey && !event.altKey && !event.metaKey && (event.key === 'M' || event.key === 'm' || event.code === 'KeyM');
}
