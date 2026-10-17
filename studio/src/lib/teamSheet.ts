// The three team sheets, Start a run, Make a team and Run one on a schedule:
// the command each writes as it is filled in, and why it cannot go yet.
// Ported from the design system's TeamSheet; kept apart from the page so a
// test can check every command and every refusal.
import { quoted } from './commands';

export type SheetKind = 'run' | 'make' | 'schedule';

export interface SheetTeam {
  name: string;
  description: string;
  nodes: number;
  lead: string;
  posture: string;
  budget?: string | null;
  doneWhen?: string[];
  template?: boolean;
  problems?: string[];
}

export interface SheetValues {
  team: string;
  goal: string;
  project: string;
  /** Empty for the team's own. */
  autonomy: string;
  rounds: string;
  usd: string;
  /** One criterion per line. */
  done: string;
  /** The project's own criteria left out of this run. */
  dropped: string[];
  /** Take the lead's recommendation after this wait; empty for never. */
  after: string;
  timeout: 'think-again-once' | 'recommend' | 'think-again';
  name: string;
  from: string;
  forProject: boolean;
  when: 'at' | 'every' | 'on';
  at: string;
  every: string;
}

export const POSTURES = [
  { name: 'manual', desc: 'Asks you at every step, and lets you change each brief' },
  { name: 'supervised', desc: 'Goes on, and holds at the gates the team names' },
  { name: 'autonomous', desc: 'Holds for nothing, within its budget and permissions' },
];

export const TIMEOUTS = [
  { name: 'think-again-once', sub: 'Back to the lead once, then its recommendation (the default)' },
  { name: 'recommend', sub: 'Take its recommendation at once' },
  { name: 'think-again', sub: 'Back to the lead every time' },
] as const;

export function sheetValues(preset: Partial<SheetValues> = {}): SheetValues {
  return {
    team: '',
    goal: '',
    project: '',
    autonomy: '',
    rounds: '',
    usd: '',
    done: '',
    dropped: [],
    after: '',
    timeout: 'think-again-once',
    name: '',
    from: '',
    forProject: false,
    when: 'every',
    at: '09:00',
    every: '24h',
    ...preset,
  };
}

const lines = (text: string) => text.split('\n').map((one) => one.trim()).filter(Boolean);

/** The command each sheet runs, as typed after `$ `. */
export const SHEET_COMMANDS: Record<SheetKind, string> = {
  run: 'loadout team run',
  make: 'loadout team new',
  schedule: 'loadout team schedule add',
};

/** The command's path, as Studio's jobs name it: 'team new'. */
export const sheetPath = (kind: SheetKind) => SHEET_COMMANDS[kind].slice('loadout '.length);

/**
 * The words after the command's path, as a job or the page's start route runs them.
 *
 * A run drops each of the project's criteria it leaves out by name, even all
 * of them: the start route takes them one by one, and naming each says the
 * same thing as --no-project-done-when.
 */
export function sheetWords(kind: SheetKind, v: SheetValues): string[] {
  if (kind === 'make') {
    return [v.name || '<name>', ...(v.from ? ['--from', v.from] : []), ...(v.forProject ? ['--for-this-project', '--project', v.project] : [])];
  }

  if (kind === 'schedule') {
    return [
      v.name || '<name>', v.team || '<team>', v.goal.trim() || '…', '--project', v.project,
      ...(v.when === 'at' ? ['--at', v.at] : v.when === 'on' ? ['--on', 'commit'] : ['--every', v.every.trim() || '24h']),
      ...(v.autonomy ? ['--autonomy', v.autonomy] : []),
      ...(v.usd ? ['--usd', v.usd] : []),
    ];
  }

  const words = [v.team || '<team>', v.goal.trim() || '…', '--project', v.project];

  if (v.autonomy) {
    words.push('--autonomy', v.autonomy);
  }

  if (v.rounds) {
    words.push('--rounds', v.rounds);
  }

  if (v.usd) {
    words.push('--usd', v.usd);
  }

  for (const criterion of lines(v.done)) {
    words.push('--done-when', criterion);
  }

  for (const criterion of v.dropped) {
    words.push('--drop-project-done-when', criterion);
  }

  if (v.after) {
    words.push('--take-recommendation-after', v.after);

    if (v.timeout !== 'think-again-once') {
      words.push('--on-timeout', v.timeout);
    }
  }

  return words;
}

/** The line the sheet shows under its form, as a person would type it. */
export function sheetCommand(kind: SheetKind, v: SheetValues): string {
  return `$ ${SHEET_COMMANDS[kind]} ${sheetWords(kind, v).map(quoted).join(' ')}`;
}

/** Why the sheet cannot go yet, or null when it can. */
export function sheetProblem(kind: SheetKind, v: SheetValues, teams: SheetTeam[]): string | null {
  if (kind === 'make') {
    if (!v.name) {
      return 'give it a name first';
    }

    if (!/^[a-z0-9]+(-[a-z0-9]+)*$/.test(v.name)) {
      return 'lowercase words joined by hyphens, as the built-in ones are';
    }

    return teams.some((team) => team.name === v.name) ? `a team called ${v.name} already exists` : null;
  }

  if (kind === 'schedule' && !v.name) {
    return 'give the schedule a name first';
  }

  const team = teams.find((one) => one.name === v.team);

  if (!team) {
    return 'pick a team first';
  }

  if (team.template) {
    return `${team.name} is a shape to copy, not a team to run`;
  }

  if (team.problems?.length) {
    return `it can’t run here: ${team.problems[0]}`;
  }

  if (!v.goal.trim()) {
    return 'say what the run is for first';
  }

  if (!v.project) {
    return 'pick a project first';
  }

  if (kind === 'schedule') {
    // Nobody is watching when a schedule fires, and a manual run asks at every step.
    if (v.autonomy === 'manual') {
      return 'a schedule cannot be manual: nobody is there to answer when it fires';
    }

    if (v.when === 'every') {
      const every = v.every.trim();

      if (!/^\d+\s*(m|h|d)$/.test(every)) {
        return 'say how often as 30m, 2h or 1d';
      }

      if (/^\d+\s*m$/.test(every) && parseInt(every, 10) < 5) {
        return 'nothing may repeat faster than every five minutes';
      }
    }
  }

  if (v.usd && !/^(none|\d+(\.\d+)?)$/i.test(v.usd.trim())) {
    return 'a budget is a figure in US dollars, or none';
  }

  if (v.usd && /^0+(\.0+)?$/.test(v.usd.trim())) {
    return 'a budget of nothing is refused; say none for no cap';
  }

  if (v.rounds && !/^\d+$/.test(v.rounds.trim())) {
    return 'rounds is a whole number';
  }

  if (v.after && !/^(\d+\s*(m|h|d|mins?|minutes?|hours?|days?)|never|off)$/i.test(v.after.trim())) {
    return 'write the wait as 30m, 2h or 1d';
  }

  return null;
}
