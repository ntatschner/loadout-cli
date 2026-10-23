// The Trigger rules page's form: the 'trigger add' command it writes as it is
// filled in, the values an existing rule reads back as for editing, and why
// the form cannot be saved yet. Kept apart from the page so a test can check
// every command and every refusal. The command's own check is the one that
// counts; this only says early what it would say anyway.
import { quoted } from './commands';

/** One event kind, as 'trigger events --json' lists it. */
export interface EventKind {
  kind: string;
  fields: string[];
  github: boolean;
  ending: boolean;
}

/** One rule, as 'trigger list --json' carries it under "rule". */
export interface RuleRead {
  id: string;
  on: string;
  projects: string[];
  every?: string | null;
  at?: string | null;
  where: Record<string, string>;
  action: { kind: string; name: string; goal: string; autonomy: string };
  budgetUsd?: number | null;
  capUsd?: number | null;
  capRuns: number;
  window: string;
  debounce: string;
  enabled: boolean;
  version: number;
  suggestedBy?: string;
}

/** A condition: the event's field must hold this value. */
export interface Condition {
  field: string;
  value: string;
}

export interface RuleValues {
  name: string;
  on: string;
  starts: 'team' | 'builtin';
  team: string;
  builtin: string;
  goal: string;
  autonomy: 'supervised' | 'autonomous';
  projects: string[];
  where: Condition[];
  /** A figure, or 'proposed' for one worked out from the team's runs. */
  capUsd: string;
  /** Empty for the team's own; a figure; or 'proposed'. */
  budgetUsd: string;
  capRuns: string;
  window: string;
  debounce: string;
  clock: 'every' | 'at';
  every: string;
  at: string;
  disabled: boolean;
}

export const RULE_DEFAULTS = { capRuns: '10', window: '24h' } as const;

export function ruleValues(preset: Partial<RuleValues> = {}): RuleValues {
  return {
    name: '',
    on: 'pr-opened',
    starts: 'team',
    team: '',
    builtin: 'docs audit',
    goal: '',
    autonomy: 'autonomous',
    projects: [],
    where: [],
    capUsd: 'proposed',
    budgetUsd: '',
    capRuns: RULE_DEFAULTS.capRuns,
    window: RULE_DEFAULTS.window,
    debounce: '',
    clock: 'every',
    every: '1h',
    at: '09:00',
    disabled: false,
    ...preset,
  };
}

/**
 * A .NET TimeSpan as JSON writes it, [d.]hh:mm:ss, in the words the command
 * takes back: whole days as 7d, whole hours as 2h, otherwise minutes.
 */
export function spanWords(span: string | null | undefined): string {
  const found = /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})/.exec(span ?? '');

  if (!found) {
    return '';
  }

  const minutes = Number(found[1] ?? 0) * 1440 + Number(found[2]) * 60 + Number(found[3]);

  if (minutes === 0) {
    return '';
  }

  if (minutes % 1440 === 0) {
    return `${minutes / 1440}d`;
  }

  return minutes % 60 === 0 ? `${minutes / 60}h` : `${minutes}m`;
}

/** What an existing rule reads back as, so the form can change it. */
export function valuesFromRule(rule: RuleRead): RuleValues {
  const team = rule.action.kind === 'team';

  return ruleValues({
    name: rule.id,
    on: rule.on,
    starts: team ? 'team' : 'builtin',
    team: team ? rule.action.name : '',
    builtin: team ? 'docs audit' : rule.action.name,
    goal: rule.action.goal,
    autonomy: rule.action.autonomy === 'supervised' ? 'supervised' : 'autonomous',
    projects: [...rule.projects],
    where: Object.entries(rule.where).map(([field, value]) => ({ field, value })),
    capUsd: rule.capUsd == null ? '' : String(rule.capUsd),
    budgetUsd: rule.budgetUsd == null ? '' : String(rule.budgetUsd),
    capRuns: String(rule.capRuns),
    // A day reads back as 1d; written as the default, saving it unchanged
    // writes no --window at all.
    window: ['', '1d'].includes(spanWords(rule.window)) ? RULE_DEFAULTS.window : spanWords(rule.window),
    debounce: spanWords(rule.debounce),
    clock: rule.at ? 'at' : 'every',
    every: spanWords(rule.every) || '1h',
    at: (rule.at ?? '09:00').slice(0, 5),
    disabled: !rule.enabled,
  });
}

/** The words after 'trigger add', as a job runs them. */
export function ruleWords(v: RuleValues): string[] {
  const words = [v.name || '<name>', v.on];

  if (v.starts === 'team') {
    words.push('--team', v.team || '<team>', '--goal', v.goal.trim() || '…', '--autonomy', v.autonomy);
  } else {
    words.push('--builtin', v.builtin);
  }

  for (const project of v.projects) {
    words.push('--project', project);
  }

  for (const one of v.where.filter((each) => each.field && each.value.trim())) {
    words.push('--where', `${one.field}=${one.value.trim()}`);
  }

  if (v.on === 'clock') {
    words.push(...(v.clock === 'at' ? ['--at', v.at] : ['--every', v.every.trim()]));
  }

  // A built-in costs nothing, so it has no money to cap.
  if (v.starts === 'team' && v.capUsd.trim()) {
    words.push('--cap-usd', v.capUsd.trim());
  }

  if (v.starts === 'team' && v.budgetUsd.trim()) {
    words.push('--budget-usd', v.budgetUsd.trim());
  }

  if (v.capRuns.trim() && v.capRuns.trim() !== RULE_DEFAULTS.capRuns) {
    words.push('--cap-runs', v.capRuns.trim());
  }

  if (v.window.trim() && v.window.trim() !== RULE_DEFAULTS.window) {
    words.push('--window', v.window.trim());
  }

  if (v.debounce.trim()) {
    words.push('--debounce', v.debounce.trim());
  }

  if (v.disabled) {
    words.push('--disabled');
  }

  return words;
}

/** The line the form shows under it, as a person would type it. */
export const ruleCommand = (v: RuleValues) => `$ loadout trigger add ${ruleWords(v).map(quoted).join(' ')}`;

const DURATION = /^\d+\s*(m|h|d)$/;
const MONEY = /^\d+(\.\d+)?$/;

/** Why the form cannot be saved yet, or null when it can. */
export function ruleProblem(v: RuleValues, kinds: EventKind[], teams: string[]): string | null {
  if (!v.name) {
    return 'give the rule a name first';
  }

  if (!/^[a-z0-9]+(-[a-z0-9]+)*$/.test(v.name)) {
    return 'a rule’s name is lowercase words joined by hyphens';
  }

  const kind = kinds.find((one) => one.kind === v.on);

  if (!kind) {
    return 'pick what it waits for';
  }

  if (v.starts === 'team') {
    if (!v.team) {
      return 'pick the team it starts';
    }

    if (!teams.includes(v.team)) {
      return `there is no team called ${v.team}`;
    }

    if (!v.goal.trim()) {
      return 'say what to ask the team for';
    }
  } else if (!v.builtin) {
    return 'pick the built-in it starts';
  }

  for (const one of v.where) {
    if (!one.field) {
      return 'pick the field each condition is about';
    }

    if (!kind.fields.includes(one.field)) {
      return `a ${v.on} event carries no ${one.field}`;
    }

    if (!one.value.trim()) {
      return `say what ${one.field} must be`;
    }
  }

  if (v.on === 'clock') {
    if (v.clock === 'every') {
      if (!DURATION.test(v.every.trim())) {
        return 'say how often as 30m, 2h or 1d';
      }

      if (/^\d+\s*m$/.test(v.every.trim()) && parseInt(v.every, 10) < 5) {
        return 'a clock rule comes round every five minutes at the most';
      }
    } else if (!/^\d{2}:\d{2}$/.test(v.at)) {
      return 'say the time of day as 09:00';
    }

    if (v.starts === 'team' && v.projects.length !== 1) {
      return 'a clock rule that starts a team names the one project it runs on';
    }
  }

  if (v.starts === 'team') {
    const cap = v.capUsd.trim();

    if (!cap) {
      return 'a rule that starts a team needs a spend cap, or proposed for one worked out from its runs';
    }

    if (cap !== 'proposed' && (!MONEY.test(cap) || Number(cap) <= 0)) {
      return 'the spend cap is a figure in US dollars above nothing, or proposed';
    }

    const budget = v.budgetUsd.trim();

    if (budget && budget !== 'proposed' && (!MONEY.test(budget) || Number(budget) <= 0)) {
      return 'the budget for each run is a figure in US dollars above nothing, or proposed';
    }

    if (budget && MONEY.test(budget) && MONEY.test(cap) && Number(budget) > Number(cap)) {
      return 'a run’s budget cannot be more than the spend cap, or not even one run could start';
    }
  }

  if (!/^\d+$/.test(v.capRuns.trim()) || Number(v.capRuns) < 1) {
    return 'the most runs in its window is a whole number, at least one';
  }

  if (!DURATION.test(v.window.trim())) {
    return 'write the window as 24h or 7d';
  }

  if (v.debounce.trim() && !DURATION.test(v.debounce.trim())) {
    return 'write how long to wait as 10m or 1h';
  }

  return null;
}
