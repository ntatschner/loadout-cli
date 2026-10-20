// Terminal Focus's tabs as this browser has arranged them: their order, the
// running sessions hidden from the strip, and the ended ones cleared off it.
// All of it is this browser's own, kept beside the layout, so arranging the
// tabs here never changes what another screen shows. The daemon holds only
// the names. Each daemon serves its own address, and the browser keeps what
// each address stores apart, so one daemon's tabs never meet another's.

export const TABS_KEY = 'studio-deck-tabs';

export interface TabState {
  /** Ids in the order the tabs were put in; sessions not here follow, in the daemon's order. */
  order: string[];
  /** Running sessions taken off the strip, still listed on the Sessions page. */
  hidden: string[];
  /** Ended sessions cleared off the strip. */
  cleared: string[];
}

export const NO_TABS: TabState = { order: [], hidden: [], cleared: [] };

function ids(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((one): one is string => typeof one === 'string') : [];
}

/** What a browser kept, or nothing arranged where it kept nothing readable. */
export function readTabs(text: string): TabState {
  try {
    const read = JSON.parse(text) as Partial<TabState> | null;

    return { order: ids(read?.order), hidden: ids(read?.hidden), cleared: ids(read?.cleared) };
  } catch {
    return { ...NO_TABS };
  }
}

/** The tabs on the strip, in this browser's order, and the hidden ones beside them. */
export function arrange<T extends { id: string; ended?: boolean }>(sessions: T[], state: TabState): { shown: T[]; hidden: T[] } {
  const byId = new Map(sessions.map((one) => [one.id, one]));
  const placed = state.order.filter((id) => byId.has(id));
  const ordered = [...placed.map((id) => byId.get(id)!), ...sessions.filter((one) => !placed.includes(one.id))];

  return {
    shown: ordered.filter((one) => !state.hidden.includes(one.id) && !(one.ended && state.cleared.includes(one.id))),
    hidden: ordered.filter((one) => state.hidden.includes(one.id) && !(one.ended && state.cleared.includes(one.id))),
  };
}

/**
 * Forgets ids for sessions the daemon no longer has. Sessions end with the
 * daemon, so without this the lists would only grow with ids that will
 * never come back.
 */
export function prune(state: TabState, present: string[]): TabState {
  const keep = (list: string[]) => list.filter((id) => present.includes(id));

  return { order: keep(state.order), hidden: keep(state.hidden), cleared: keep(state.cleared) };
}

/** The strip's order with one tab put before another, or at the end for null. */
export function place(shown: string[], id: string, before: string | null): string[] {
  const without = shown.filter((one) => one !== id);
  const at = before === null ? without.length : without.indexOf(before);

  return at < 0 ? shown : [...without.slice(0, at), id, ...without.slice(at)];
}

/** The strip's order with one tab moved along it by a step, kept at either end. */
export function nudge(shown: string[], id: string, by: number): string[] {
  const from = shown.indexOf(id);
  const to = Math.max(0, Math.min(shown.length - 1, from + by));

  if (from < 0 || to === from) {
    return shown;
  }

  const without = shown.filter((one) => one !== id);

  return [...without.slice(0, to), id, ...without.slice(to)];
}

/** A new order for the strip, keeping where the hidden ones were among the rest. */
export function withOrder(state: TabState, shown: string[]): TabState {
  const rest = state.order.filter((id) => !shown.includes(id));

  return { ...state, order: [...shown, ...rest] };
}

export function hide(state: TabState, id: string): TabState {
  return state.hidden.includes(id) ? state : { ...state, hidden: [...state.hidden, id] };
}

export function unhide(state: TabState, id: string): TabState {
  return { ...state, hidden: state.hidden.filter((one) => one !== id) };
}

export function clear(state: TabState, id: string): TabState {
  return state.cleared.includes(id) ? state : { ...state, cleared: [...state.cleared, id], hidden: state.hidden.filter((one) => one !== id) };
}
