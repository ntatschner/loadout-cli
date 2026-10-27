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
  /** Whether it has ended: closing its tab then clears it rather than asking. */
  ended?: boolean;
  /** What it was launched as, for going back to after a rename. */
  launchTitle?: string;
  /** Whether its agent is known to be given its name as well. */
  namesAgent?: boolean;
  /** 'launch' for an agent session, 'command' for a command that asks as it goes. */
  kind?: 'launch' | 'command';
  /** The project it runs in, for saying what ending it ends. */
  project?: string | null;
  /** How much of its context window is in use, as its status line last said; null where it has not. */
  context?: number | null;
}

export type DeckLayout = 1 | 2 | 4;

/** The session in each pane of a layout, in reading order; null where a pane has nothing yet. */
export type DeckSlots = (string | null)[];

/**
 * Which session each pane shows. Panes keep their sessions: a pick already
 * in a pane changes nothing, and one that is not takes the pane at `at`, the
 * highlighted one, or else the first empty one, moving no other. A layout
 * that grows fills its new panes in tab order; one that shrinks drops panes
 * from the end, though a pick in one of them keeps the last pane. A session
 * that has ended, or gone from the list, stays until something takes its place.
 */
export function placeSlots(slots: DeckSlots, o: { order: string[]; layout: DeckLayout; pick?: string | null; at?: number; fill?: number }): DeckSlots {
  const out = padSlots(slots, o.layout);

  if (o.pick && !out.includes(o.pick)) {
    const at = o.at ?? -1;
    const empty = out.indexOf(null);
    out[at >= 0 ? Math.min(at, o.layout - 1) : Math.max(0, empty)] = o.pick;
  }

  const spare = o.order.filter((id) => !out.includes(id));
  // Only the first `fill` panes take spare tabs: the ones a layout grown for
  // a launch added wait empty for the next launch.
  const fill = o.fill ?? o.layout;

  return out.map((id, i) => id ?? (i < fill ? spare.shift() : null) ?? null);
}

/** The panes of a layout, each session in at most one, with nulls where a pane has nothing. */
function padSlots(slots: DeckSlots, layout: DeckLayout): DeckSlots {
  const out = slots.slice(0, layout).map((id, i, all) => (id && all.indexOf(id) === i ? id : null));

  while (out.length < layout) {
    out.push(null);
  }

  return out;
}

/** Where a newly started session went, and what made room for it. */
export interface LaunchPlace {
  slots: DeckSlots;
  layout: DeckLayout;
  /** The pane it is in, by its place. */
  at: number;
  /** Whether the layout grew to make a pane for it. */
  grew: boolean;
  /** The session whose pane it took, which stays a tab; null where it took an empty pane. */
  replaced: string | null;
  /** Why that pane gave way. */
  because: 'ended' | 'idle' | null;
}

/**
 * Where a session just started goes: an empty pane, which a session gone
 * from the list leaves its pane; else a layout grown
 * 1→2→4 to make one; else, with four full, the pane of a session that has
 * ended, or else the one idle longest. A session waiting on you is never the
 * idle one pushed out, unless every pane is waiting. A session already in a
 * pane stays where it is.
 */
export function launchSlots(
  slots: DeckSlots,
  o: { layout: DeckLayout; pick: string; ended?: string[]; gone?: string[]; waiting?: string[]; active?: Record<string, number> },
): LaunchPlace {
  const out = padSlots(slots, o.layout).map((id) => (id && (o.gone ?? []).includes(id) ? null : id));
  const already = out.indexOf(o.pick);

  if (already >= 0) {
    return { slots: out, layout: o.layout, at: already, grew: false, replaced: null, because: null };
  }

  const empty = out.indexOf(null);

  if (empty >= 0) {
    out[empty] = o.pick;

    return { slots: out, layout: o.layout, at: empty, grew: false, replaced: null, because: null };
  }

  if (o.layout < 4) {
    const layout: DeckLayout = o.layout === 1 ? 2 : 4;
    const grown = padSlots(out, layout);
    const at = grown.indexOf(null);
    grown[at] = o.pick;

    return { slots: grown, layout, at, grew: true, replaced: null, because: null };
  }

  const ended = out.findIndex((id) => !!id && (o.ended ?? []).includes(id));
  let at = ended;

  if (at < 0) {
    const quiet = (i: number) => o.active?.[out[i] ?? ''] ?? 0;
    const every = out.map((_, i) => i);
    const free = every.filter((i) => !(o.waiting ?? []).includes(out[i] ?? ''));
    const choices = free.length ? free : every;
    at = choices.reduce((best, i) => (quiet(i) < quiet(best) ? i : best), choices[0]);
  }

  const replaced = out[at];
  out[at] = o.pick;

  return { slots: out, layout: o.layout, at, grew: false, replaced, because: ended >= 0 ? 'ended' : 'idle' };
}

/**
 * Gives back panes a launch added once the sessions in them have gone: the
 * smallest layout, never below the one chosen by hand, that holds the
 * sessions still listed, in their reading order. A layout no larger than the
 * one chosen is left alone.
 */
export function shrinkSlots(slots: DeckSlots, o: { layout: DeckLayout; chosen: DeckLayout; order: string[] }): { slots: DeckSlots; layout: DeckLayout } {
  if (o.layout <= o.chosen) {
    return { slots, layout: o.layout };
  }

  const kept = padSlots(slots, o.layout).filter((id): id is string => !!id && o.order.includes(id));
  const layout = ([1, 2, 4] as DeckLayout[]).find((one) => one >= o.chosen && one >= kept.length) ?? 4;

  return layout >= o.layout ? { slots, layout: o.layout } : { slots: padSlots(kept, layout), layout };
}

/**
 * Terminal Focus's own keys: Ctrl+Alt+1–9 picks a session by its tab, and
 * Ctrl+Alt+arrows move between the panes shown, in the order they sit. Null
 * where the key is not one of them.
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
  /** A session or a team run on Home; on the six pages, whatever the page lists. */
  kind: string;
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

/** The six pages that follow the mode: beside the deck or the board, quiet in Minimal, a desk in Pure Focus. */
export const SIX_PAGES = ['ideas', 'projects', 'context', 'settings', 'memory', 'spend'] as const;

export type SixPage = (typeof SIX_PAGES)[number];

export const isSixPage = (page: string | null | undefined): page is SixPage => (SIX_PAGES as readonly string[]).includes(page ?? '');

export const PAGE_TITLES: Record<SixPage, string> = { ideas: 'Ideas', projects: 'Projects', context: 'Context', settings: 'Settings', memory: 'Memory', spend: 'Spend' };

/**
 * Where closing a page opened beside goes back to: the last route that was
 * not one of the six pages, nor anything else the mode opened beside, kept
 * as each route is shown, or Home.
 */
export function besideReturn(kept: string[], route: { page: string; rest: string[] }, beside = false): string[] {
  return beside || isSixPage(route.page) || route.page === 'unknown' ? kept : [route.page, ...route.rest];
}
