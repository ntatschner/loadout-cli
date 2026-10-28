// What Home's Map and Lens draw: three filters, kind, state and project, kept
// as studio-map-kinds, studio-map-states and studio-map-projects. Kinds and
// states name what to show; projects names what to hide, so a project added
// later shows without anyone choosing it. Pure, so the presets can be pinned
// to what the map drew before there were filters.
import type { Preferences } from './api';
import type { Work, WorkKind } from './lines';

export type MapKind = 'runs' | 'sessions' | 'ideas' | 'tasks';
export type MapState = 'waiting' | 'running' | 'failed' | 'ended';

export const KINDS: readonly MapKind[] = ['runs', 'sessions', 'ideas', 'tasks'];
export const STATES: readonly MapState[] = ['waiting', 'running', 'failed', 'ended'];

/** How the project filter names work that belongs to no project, in the bar and in the key alike. */
export const NO_PROJECT = '(no project)';

export const KIND_NAMES: Record<MapKind, string> = { runs: 'Team runs', sessions: 'Sessions', ideas: 'Ideas', tasks: 'Tasks' };
export const STATE_NAMES: Record<MapState, string> = { waiting: 'Waiting on you', running: 'Running', failed: 'Failed', ended: 'Ended' };

const KIND_OF: Record<WorkKind, MapKind> = { 'team run': 'runs', session: 'sessions', idea: 'ideas', task: 'tasks' };

export interface MapFilter {
  kinds: MapKind[];
  states: MapState[];
  /** Projects to hide, NO_PROJECT among them for work outside any project. */
  projects: string[];
}

export type PresetName = 'inflight' | 'runs' | 'all';

/**
 * In flight draws what the map drew before there were filters: no ideas, and
 * nothing settled. Runs and sessions is the same with tasks off as well.
 * Everything is every line Home has, which is what linesOf gives.
 */
export const PRESETS: Record<PresetName, MapFilter> = {
  inflight: { kinds: ['runs', 'sessions', 'tasks'], states: ['waiting', 'running', 'failed'], projects: [] },
  runs: { kinds: ['runs', 'sessions'], states: ['waiting', 'running', 'failed'], projects: [] },
  all: { kinds: [...KINDS], states: [...STATES], projects: [] },
};

export const PRESET_NAMES: Record<PresetName, string> = { inflight: 'In flight', runs: 'Runs and sessions', all: 'Everything' };

const DAY = 24 * 60;

// Rounded as linesOf rounds, so a failure leaves the map at the minute it always did.
const minutesSince = (iso: string | null | undefined, now: number) => (iso ? Math.max(0, Math.round((now - Date.parse(iso)) / 60000)) : Infinity);

/**
 * Which of the four states a line is in. Waiting on you is the line's own
 * `waiting`, the flag the header counts. A failure is failed for a day and
 * ended after that. Ideas being shaped and tasks being done or blocked are
 * running; an accepted idea and a finished task are ended.
 */
export function stateOf(line: Work, now: number): MapState {
  if (line.waiting) {
    return 'waiting';
  }

  if (line.run) {
    const run = line.run;

    if (run.running || run.waitingForYou) {
      return 'running';
    }

    return run.outcome === 'failed' && minutesSince(run.finished, now) < DAY ? 'failed' : 'ended';
  }

  if (line.session) {
    const session = line.session;

    if (!session.ended) {
      return 'running';
    }

    // No exit code reads as a clean end, and no end time as a day ago: a
    // session the daemon lost track of is not a failure to keep in view.
    return session.exitCode && minutesSince(session.endedAt, now) < DAY ? 'failed' : 'ended';
  }

  if (line.idea) {
    return line.idea.detail.stage === 'accepted' ? 'ended' : 'running';
  }

  return line.task?.state === 'done' ? 'ended' : 'running';
}

/** The name the project filter knows a line by. */
export const projectOf = (line: Work) => line.project ?? NO_PROJECT;

/** Whether the filters let a line through. */
export function shows(filter: MapFilter, line: Work, now: number): boolean {
  return filter.kinds.includes(KIND_OF[line.kind]) && filter.states.includes(stateOf(line, now)) && !filter.projects.includes(projectOf(line));
}

/**
 * What the filters draw, coded again in drawing order as linesOf codes them
 * so the lines in view keep eight apart, and what they hide.
 */
export function filterLines(lines: Work[], filter: MapFilter, now = Date.now()): { drawn: Work[]; hidden: Work[] } {
  const drawn: Work[] = [];
  const hidden: Work[] = [];

  for (const line of lines) {
    (shows(filter, line, now) ? drawn : hidden).push(line);
  }

  return { drawn: drawn.map((line, index) => ({ ...line, code: String((index % 8) + 1) })), hidden };
}

/** 'N hidden, M waiting on you', or 'N hidden' when none of them waits. Empty when nothing is. */
export function hiddenNote(hidden: Work[]): string {
  if (!hidden.length) {
    return '';
  }

  const waiting = hidden.filter((line) => line.waiting).length;

  return `${hidden.length} hidden${waiting ? `, ${waiting} waiting on you` : ''}`;
}

const listOf = (value: string | null | undefined) =>
  (value ?? '').split(',').map((part) => part.trim()).filter(Boolean);

/**
 * The filters as config holds them. A kind or state key that is unset, or
 * names nothing this page knows, reads as In flight's, so the map is never
 * blank by accident. A daemon older than the three keys sends only the old
 * scope, which still picks its preset.
 */
export function filterOf(prefs: Pick<Preferences, 'mapKinds' | 'mapStates' | 'mapProjects' | 'mapScope'> | null | undefined): MapFilter {
  if (prefs && prefs.mapKinds === undefined && prefs.mapScope === 'all') {
    return PRESETS.all;
  }

  const kinds = KINDS.filter((kind) => listOf(prefs?.mapKinds).includes(kind));
  const states = STATES.filter((state) => listOf(prefs?.mapStates).includes(state));

  return {
    kinds: kinds.length ? kinds : PRESETS.inflight.kinds,
    states: states.length ? states : PRESETS.inflight.states,
    projects: listOf(prefs?.mapProjects),
  };
}

const same = (a: readonly string[], b: readonly string[]) => a.length === b.length && a.every((item) => b.includes(item));

/** The preset the filters match, or null for Custom. */
export function presetOf(filter: MapFilter): PresetName | null {
  const found = (Object.keys(PRESETS) as PresetName[]).find((name) => {
    const preset = PRESETS[name];

    return same(filter.kinds, preset.kinds) && same(filter.states, preset.states) && same(filter.projects, preset.projects);
  });

  return found ?? null;
}

/** The three keys and the values that write a filter, in the order the command line would take them. */
export function keysOf(filter: MapFilter): [string, string][] {
  return [
    ['studio-map-kinds', filter.kinds.join(', ')],
    ['studio-map-states', filter.states.join(', ')],
    ['studio-map-projects', filter.projects.join(', ')],
  ];
}

/**
 * One chip turned on or off. The last kind or state on stays on: a filter
 * showing nothing cannot be reached from the bar, as it cannot from config.
 */
export function toggled<T extends string>(on: readonly T[], item: T, order: readonly T[]): T[] {
  if (on.includes(item)) {
    return on.length > 1 ? on.filter((one) => one !== item) : [...on];
  }

  return order.filter((one) => one === item || on.includes(one));
}

/** Every project the bar offers: those with work in view, then any hidden that have none, '(no project)' last. */
export function projectsIn(lines: Work[], hidden: readonly string[]): string[] {
  const named = new Set<string>();

  for (const line of lines) {
    named.add(projectOf(line));
  }

  for (const project of hidden) {
    named.add(project);
  }

  const list = [...named].filter((project) => project !== NO_PROJECT).sort((a, b) => a.localeCompare(b));

  return named.has(NO_PROJECT) ? [...list, NO_PROJECT] : list;
}
