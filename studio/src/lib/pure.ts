// Pure Focus without a page: what the list holds and how it is grouped and
// scoped, the keys and what each does, and the actions for each kind of
// thing, built from the same pieces the other pages use, so every action
// runs the same command it runs anywhere else.
import type { Run } from './api';
import { matchesWords } from './findWords';
import type { Work } from './lines';
import { wordFor } from './signals';
import { runWord } from './teamsData';

/** One thing in the list: a session, a team run, an idea or a task. */
export interface PureItem {
  /** Unique across kinds, for the list and the browser's memory of the last picked. */
  id: string;
  /** The id a command names. */
  ref: string;
  /** A session, team run, idea or task on Home's desk; on a page's desk, whatever that page lists. */
  kind: string;
  title: string;
  meta: string;
  /** Its state word; empty for something with no state, such as a section of Settings. */
  word: string;
}

export interface PureGroup {
  key: string;
  name: string;
  items: PureItem[];
}

/** One action, numbered by its place in the pane. */
export interface PureAction {
  label: string;
  /** The command it runs, exactly as the other pages show it, or what it does where it runs none. */
  cmd?: string;
  kind?: '' | 'needs' | 'danger';
  disabledReason?: string;
  /** Runs it; a string returned is said back. */
  run?: () => unknown;
}

/** A match in the command bar. */
export interface PureCommand {
  kind: 'action' | 'go' | 'command';
  label: string;
  cmd?: string;
  run?: () => void;
}

export type PureScope = 'all' | 'needs' | 'sessions' | 'runs' | 'ideas' | 'tasks';

/** G then a letter: where Home's list goes. */
export const GO_KEYS: Record<string, PureScope> = { h: 'all', s: 'sessions', t: 'runs', k: 'tasks', n: 'needs' };

/** G then a letter: a page, as its own desk. U for usage (Spend), O for options (Settings). */
export const PAGE_KEYS: Record<string, 'ideas' | 'projects' | 'context' | 'memory' | 'spend' | 'settings'> = { i: 'ideas', p: 'projects', c: 'context', m: 'memory', u: 'spend', o: 'settings' };

export const SCOPE_WORDS: Record<PureScope, string> = {
  all: 'everything',
  needs: 'what needs you',
  sessions: 'sessions',
  runs: 'team runs',
  ideas: 'ideas',
  tasks: 'tasks',
};

export const isScope = (value: string | null | undefined): value is PureScope => !!value && value in SCOPE_WORDS;

/** Every key in Pure Focus, as the key help lists them. */
export const PURE_KEYS: [string, [string[], string][]][] = [
  ['Move', [[['J', '↓'], 'next in the list'], [['K', '↑'], 'previous'], [['Home'], 'first'], [['End'], 'last'], [['/'], 'filter the list']]],
  ['Open and act', [[['Enter'], 'open the picked one in the view'], [['Esc'], 'back to the list'], [['1'], 'to 9: run that action'], [['Ctrl', ']'], 'leave a terminal']]],
  ['Go', [[['G', 'H'], 'Home: everything'], [['G', 'S'], 'sessions'], [['G', 'T'], 'team runs'], [['G', 'K'], 'tasks'], [['G', 'N'], 'what needs you']]],
  ['Go to a page', [[['G', 'I'], 'Ideas'], [['G', 'P'], 'Projects'], [['G', 'C'], 'Context'], [['G', 'M'], 'Memory'], [['G', 'U'], 'Spend (usage)'], [['G', 'O'], 'Settings (options)']]],
  ['Everywhere', [[[':'], 'the command bar'], [['F6'], 'the next pane'], [['?'], 'this list'], [['Ctrl', 'Shift', 'M'], 'change mode'], [['Ctrl', 'K'], 'the palette']]],
];

/**
 * The list, grouped: what needs you first, then sessions, team runs, ideas
 * and tasks. A scope keeps only its own group; what needs you is a scope of
 * its own, so G then S shows the sessions that are waiting among the rest.
 */
export function pureGroups(lines: Work[], runs: Run[], idleAfter: number, scope: PureScope = 'all'): PureGroup[] {
  const waiting = new Set<string>();
  const sessions: PureItem[] = [];
  const teamRuns: PureItem[] = [];
  const ideas: PureItem[] = [];
  const tasks: PureItem[] = [];

  for (const line of lines) {
    if (line.kind === 'session') {
      sessions.push({
        id: line.id,
        ref: line.ref,
        kind: 'session',
        title: line.title,
        meta: [line.session?.project, line.session?.kind === 'command' ? 'loadout' : line.session?.agent, line.session?.mode].filter(Boolean).join(' · '),
        word: line.stage === 4 ? 'ended' : wordFor(line, idleAfter),
      });

      if (line.waiting) {
        waiting.add(line.id);
      }
    } else if (line.kind === 'idea') {
      ideas.push({ id: line.id, ref: line.ref, kind: 'idea', title: line.title, meta: line.where ?? '', word: wordFor(line, idleAfter) });
    } else if (line.kind === 'task') {
      tasks.push({ id: line.id, ref: line.ref, kind: 'task', title: line.title, meta: line.where ?? '', word: wordFor(line, idleAfter) });
    }
  }

  for (const run of runs) {
    const id = `run:${run.id}`;
    teamRuns.push({ id, ref: run.id, kind: 'team run', title: run.goal, meta: [run.team, run.project, `round ${run.rounds}`].filter(Boolean).join(' · '), word: runWord(run) });

    if (run.waitingForYou) {
      waiting.add(id);
    }
  }

  const needs = [...sessions, ...teamRuns].filter((item) => waiting.has(item.id));

  if (scope === 'needs') {
    return [{ key: 'needs', name: 'Needs you', items: needs }];
  }

  // Everything: what needs you once, at the top, and not again in its own group.
  const rest = (items: PureItem[]) => (scope === 'all' ? items.filter((item) => !waiting.has(item.id)) : items);
  const all: PureGroup[] = [
    { key: 'needs', name: 'Needs you', items: scope === 'all' ? needs : [] },
    { key: 'sessions', name: 'Sessions', items: rest(sessions) },
    { key: 'runs', name: 'Team runs', items: rest(teamRuns) },
    { key: 'ideas', name: 'Ideas', items: ideas },
    { key: 'tasks', name: 'Tasks', items: tasks },
  ];

  return scope === 'all' ? all : all.filter((group) => group.key === scope);
}

/** What the filter keeps: each of its words in the title or the facts beside it, as findWords reads them. */
export function filtered(groups: PureGroup[], filter: string): PureGroup[] {
  return groups.map((group) => ({ ...group, items: group.items.filter((item) => matchesWords([item.title, item.meta], filter)) })).filter((group) => group.items.length);
}

/** The matches the command bar shows for what is typed. */
export function matching(rows: PureCommand[], typed: string): PureCommand[] {
  const q = typed.trim().toLowerCase();

  return rows.filter((row) => !q || `${row.label} ${row.cmd ?? ''} ${row.kind}`.toLowerCase().includes(q));
}

/** A key that names nothing here: a modifier held, or typing in a box, a dialog or a terminal. */
export function typingIn(target: EventTarget | null): boolean {
  const node = target as HTMLElement | null;

  return !!node && (node.tagName === 'INPUT' || node.tagName === 'TEXTAREA' || node.tagName === 'SELECT' || node.isContentEditable || !!node.closest?.('.ls-term, .xterm, .ls-dialog'));
}
