// The six pages as Pure Focus desks: what each lists, and the numbered
// actions for the picked thing, each with the command it runs, built with
// lineFor as every page builds them. The pages themselves are the views.
import { lineFor } from './commandLine';
import { acceptWords, stateWord } from './ideas';
import type { PureAction, PureGroup, PureItem } from './pure';

/** What an action does, given by the desk: run a command as a job, read one, open a terminal for one, or go somewhere. */
export interface DeskContext {
  /** Runs a command as a job; says what happened. */
  act: (path: string, words: string[]) => Promise<string>;
  /** Runs a command that only reads, and says what it said. */
  read: (path: string, words: string[]) => Promise<string>;
  /** Opens a command that asks as it goes in a terminal the daemon hosts. */
  terminal: (path: string, words: string[]) => Promise<string>;
  /** Goes to a route. */
  go: (...parts: string[]) => void;
  /** Why changes are refused, when Studio is only watching. */
  refused?: string;
}

const item = (id: string, kind: string, title: string, meta: string, word = ''): PureItem => ({ id, ref: id, kind, title, meta, word });

/* ---------- Ideas ---------- */

export interface DeskIdea {
  id: string;
  title: string;
  project?: string | null;
  stage: string;
  unanswered: number;
  running: boolean;
  failure?: { error: string } | null;
}

/** Needs you (waiting or failed) first, then by project. */
export function ideaGroups(ideas: DeskIdea[]): PureGroup[] {
  const open = ideas.filter((one) => one.stage !== 'accepted');
  const needs = (one: DeskIdea) => ['waiting for you', 'last round failed'].includes(stateWord(one));
  const toItem = (one: DeskIdea) => item(`${one.project ?? '~'}/${one.id}`, 'idea', one.title, `${one.project ?? 'workspace-wide'} · ${one.stage}`, stateWord(one));
  const lists = new Map<string, DeskIdea[]>();

  for (const one of open.filter((idea) => !needs(idea))) {
    const key = one.project ?? '~';
    lists.set(key, [...(lists.get(key) ?? []), one]);
  }

  return [
    { key: 'needs', name: 'Needs you', items: open.filter(needs).map(toItem) },
    ...[...lists.entries()].map(([key, ones]) => ({ key, name: key === '~' ? 'Workspace-wide' : key, items: ones.map(toItem) })),
  ].filter((group) => group.items.length);
}

export function ideaActions(one: DeskIdea, placement: { slug: string; isNew: boolean; name: string } | null, ctx: DeskContext): PureAction[] {
  const where = one.project ? ['--project', one.project] : ['--global'];
  const refine = [one.id, ...where];
  const out: PureAction[] = [];

  if (one.failure) {
    out.push(
      { label: 'Try the round again', cmd: lineFor('idea refine', refine), disabledReason: ctx.refused, run: () => ctx.act('idea refine', refine) },
      { label: 'Clear the marker', cmd: lineFor('idea dismiss', refine), disabledReason: ctx.refused, run: () => ctx.act('idea dismiss', refine) },
    );
  } else {
    out.push({ label: 'Take it one round further', cmd: lineFor('idea refine', refine), disabledReason: ctx.refused, run: () => ctx.act('idea refine', refine) });
  }

  if (placement) {
    const words = acceptWords(one.id, where, placement.isNew ? { newProject: placement.name } : { project: placement.slug });

    out.push({ label: `Accept it onto ${placement.isNew ? `a new project, ${placement.name}` : placement.slug}`, kind: 'needs', cmd: lineFor('idea accept', words), disabledReason: ctx.refused, run: () => ctx.act('idea accept', words) });
  } else {
    out.push({ label: 'Accept it', disabledReason: 'there is no plan to accept yet' });
  }

  out.push(
    { label: 'Forget this idea', kind: 'danger', cmd: lineFor('idea remove', refine), disabledReason: ctx.refused, run: () => ctx.act('idea remove', refine) },
    { label: 'Drop a new idea in', cmd: 'opens the box · records nothing yet', run: () => ctx.go('ideas', '+add') },
  );

  return out;
}

/* ---------- Projects ---------- */

export interface DeskProject {
  id: string;
  name: string;
  available: boolean;
  path?: string;
  defaultAgent?: string;
}

/** On this machine, then not on this machine. */
export function projectGroups(projects: DeskProject[]): PureGroup[] {
  return [
    { key: 'here', name: 'On this machine', items: projects.filter((one) => one.available).map((one) => item(one.id, 'project', one.name, `${one.id} · ${one.defaultAgent ?? ''}`)) },
    { key: 'away', name: 'Not on this machine', items: projects.filter((one) => !one.available).map((one) => item(one.id, 'project', one.name, one.id, 'not on this machine')) },
  ].filter((group) => group.items.length);
}

export function projectActions(one: DeskProject, fixable: number | null, ctx: DeskContext & { forget: () => void }): PureAction[] {
  const out: PureAction[] = one.available
    ? [
        { label: 'Open it in the editor', cmd: lineFor('code', [one.id]), disabledReason: ctx.refused, run: () => ctx.act('code', [one.id]) },
        { label: 'Open its folder', cmd: lineFor('project open', [one.id]), disabledReason: ctx.refused, run: () => ctx.act('project open', [one.id]) },
      ]
    : [{ label: 'Clone it onto this machine', cmd: lineFor('project clone', [one.id]), disabledReason: ctx.refused, run: () => ctx.act('project clone', [one.id]) }];

  if (fixable) {
    out.push({ label: `Put ${fixable} right`, kind: 'needs', cmd: lineFor('drift', [one.id, '--fix', '--yes']), disabledReason: ctx.refused, run: () => ctx.act('drift', [one.id, '--fix', '--yes']) });
  }

  out.push(
    { label: 'Onboard it', cmd: `${lineFor('project onboard', [one.id])} · opens a hosted terminal`, disabledReason: ctx.refused, run: () => ctx.terminal('project onboard', [one.id]) },
    // Opening the dialog changes nothing, so it takes one press; the dialog asks before anything is forgotten.
    { label: 'Forget project…', cmd: 'opens the dialog · runs nothing yet', run: () => ctx.forget() },
  );

  return out;
}

/* ---------- Context ---------- */

export const CONTEXT_SECTIONS: [string, string][] = [
  ['load', 'What a session loads'],
  ['specialists', 'Specialists'],
  ['rules', 'Rules and styles'],
  ['docs', 'Documents'],
  ['health', 'Health'],
  ['voice', 'Your writing voice'],
  ['handoff', 'Handing over'],
];

/** The sections of the chosen project, then its specialists. Neither has a state. */
export function contextGroups(slug: string, specialists: { id: string; title: string; kind: string }[]): PureGroup[] {
  // Nothing until the project is known: a section without one would read the wrong project.
  if (!slug) {
    return [];
  }

  return [
    { key: 'sections', name: slug, items: CONTEXT_SECTIONS.map(([key, title]) => item(`section:${key}`, 'section', title, slug)) },
    { key: 'specialists', name: 'Specialists', items: specialists.map((one) => item(`spec:${one.id}`, 'specialist', one.title, `${one.kind} · ${one.id}`)) },
  ].filter((group) => group.items.length);
}

export function contextActions(picked: PureItem, slug: string, ctx: DeskContext & { another: () => void }): PureAction[] {
  const reads = (label: string, path: string, words: string[]): PureAction => ({ label, cmd: `${lineFor(path, words)} · reads only`, run: () => ctx.read(path, words) });
  const changes = (label: string, path: string, words: string[]): PureAction => ({ label, cmd: lineFor(path, words), disabledReason: ctx.refused, run: () => ctx.act(path, words) });
  const section = picked.id.startsWith('section:') ? picked.id.slice('section:'.length) : null;
  const out: PureAction[] = [];

  if (section === 'load') {
    for (const mode of ['implement', 'advise', 'investigate', 'review']) {
      out.push(reads(`Explain it in ${mode} mode`, 'instructions explain', ['--project', slug, '--mode', mode]));
    }
  } else if (section === 'specialists') {
    out.push(reads('Check the library', 'instructions validate', [slug]));
  } else if (section === 'rules') {
    out.push(reads('Audit the rules', 'rules audit', [slug]));
  } else if (section === 'docs') {
    out.push(changes('Bring the symbol index up to date', 'docs refresh', ['--project', slug]));
  } else if (section === 'health') {
    out.push(reads('Check it against its specialists', 'instructions audit', ['--project', slug]));
  } else if (section === 'voice') {
    out.push(changes('Render it into the style specialist', 'voice render', ['--project', slug]));
  } else if (section === 'handoff') {
    out.push({ label: 'Write or read a handoff', disabledReason: 'a handoff is written in its form, in the view' });
  } else {
    const id = picked.id.slice('spec:'.length);

    out.push(changes('How often sessions did what it asks', 'instructions probe', [id]));
  }

  out.push({ label: 'The same for another project', cmd: 'the next project · changes nothing', run: () => ctx.another() });

  return out;
}

/* ---------- Settings ---------- */

export const SETTINGS_SECTIONS: [string, string][] = [
  ['studio', 'Studio'],
  ['loadout', "Loadout's settings"],
  ['agents', 'Agents and packs'],
  ['safety', 'Backups and safety'],
  ['daemon', 'The daemon'],
  ['machine', 'This machine'],
];

export function settingsGroups(): PureGroup[] {
  return [{ key: 'sections', name: 'Settings', items: SETTINGS_SECTIONS.map(([key, title]) => item(key, 'section', title, `#settings/${key}`)) }];
}

export interface SettingsFacts {
  /** Whether the schedules are held; null when the daemon does not say. */
  paused: boolean | null;
  autostart: boolean | null;
  unapproved: string[];
}

export function settingsActions(section: string, facts: SettingsFacts, ctx: DeskContext & { setting: (key: string, value: string) => Promise<string>; signOut: () => Promise<string> }): PureAction[] {
  const changes = (label: string, path: string, words: string[], kind: PureAction['kind'] = ''): PureAction => ({ label, kind, cmd: lineFor(path, words), disabledReason: ctx.refused, run: () => ctx.act(path, words) });
  const inForms: PureAction = { label: "Its changes are in its forms", disabledReason: 'each one is changed where it is shown, in the view' };

  switch (section) {
    case 'studio':
      return [
        { label: 'Reset the Lens', cmd: lineFor('config set', ['studio-layout-lens', '']), disabledReason: ctx.refused, run: () => ctx.setting('studio-layout-lens', '') },
        { label: 'Reset the Map', cmd: lineFor('config set', ['studio-layout-map', '']), disabledReason: ctx.refused, run: () => ctx.setting('studio-layout-map', '') },
        { label: 'Sign out every browser', kind: 'danger', cmd: lineFor('dashboard sign-out', []), disabledReason: ctx.refused, run: () => ctx.signOut() },
      ];
    case 'agents':
      return facts.unapproved.length ? facts.unapproved.map((name) => changes(`Approve ${name}`, 'pack approve', [name])) : [{ label: 'Approve a pack', disabledReason: 'no pack waits to be approved here' }];
    case 'daemon':
      return [
        ...(facts.paused !== true ? [changes('Hold the schedules', 'daemon pause', [])] : []),
        ...(facts.paused !== false ? [changes('Let the schedules fire', 'daemon resume', [])] : []),
        changes('Restart it', 'daemon restart', []),
        changes('Stop it', 'daemon stop', [], 'danger'),
        facts.autostart ? changes('Stop it starting at log-in', 'daemon autostart disable', []) : changes('Start it at log-in', 'daemon autostart enable', []),
      ];
    case 'machine':
      return [
        changes('Put right what can be put right', 'doctor', ['--fix', '--yes']),
        { label: 'Set the launcher up again', cmd: `${lineFor('setup', [])} · opens a hosted terminal`, disabledReason: ctx.refused, run: () => ctx.terminal('setup', []) },
      ];
    default:
      return [inForms];
  }
}

/* ---------- Memory ---------- */

export const STORE_NAMES: Record<string, string> = { project: 'This project', user: 'Your work, any project', machine: 'This machine only' };

export function memoryGroups(topics: { name: string; scope: string; description?: string }[]): PureGroup[] {
  return Object.entries(STORE_NAMES)
    .map(([scope, name]) => ({ key: scope, name, items: topics.filter((one) => one.scope === scope).map((one) => item(`${scope}/${one.name}`, 'topic', one.name, one.description ?? '')) }))
    .filter((group) => group.items.length);
}

export function memoryActions(picked: PureItem, slug: string, ctx: DeskContext & { another: () => void }): PureAction[] {
  const [scope, name] = [picked.id.slice(0, picked.id.indexOf('/')), picked.id.slice(picked.id.indexOf('/') + 1)];
  const out: PureAction[] = [];

  if (scope === 'project') {
    out.push({ label: 'Delete this topic', kind: 'danger', cmd: lineFor('memory delete', [name, slug, '--yes']), disabledReason: ctx.refused, run: () => ctx.act('memory delete', [name, slug, '--yes']) });
  }

  out.push(
    { label: 'Rebuild the index', cmd: lineFor('memory reindex', [slug]), disabledReason: ctx.refused, run: () => ctx.act('memory reindex', [slug]) },
    { label: 'Clean up what the audit found', cmd: lineFor('memory audit', [slug, '--clean', '--apply']), disabledReason: ctx.refused, run: () => ctx.act('memory audit', [slug, '--clean', '--apply']) },
    { label: 'Walk the topics nobody has revisited', cmd: `${lineFor('memory review', [slug])} · opens a hosted terminal`, disabledReason: ctx.refused, run: () => ctx.terminal('memory review', [slug]) },
    { label: "Another project's memory", cmd: 'the next project · changes nothing', run: () => ctx.another() },
  );

  return out;
}

/* ---------- Spend ---------- */

export const SPEND_BLOCKS: [string, string][] = [
  ['usage', 'Tokens'],
  ['where', 'Where it went'],
  ['now', 'Running now'],
  ['launches', 'Launched this week'],
  ['sessions', 'Recent sessions'],
  ['reported', 'What agents reported'],
  ['machine', 'This machine'],
];

export function spendGroups(): PureGroup[] {
  return [{ key: 'blocks', name: 'Spend', items: SPEND_BLOCKS.map(([key, title]) => item(key, 'block', title, '')) }];
}

export const PERIOD_WORDS: Record<string, string> = { '1': 'day', '7': 'week', '30': '30 days', '90': '90 days' };

export function spendActions(days: string, by: string, ctx: DeskContext & { period: (days: string) => void; group: (by: string) => void }): PureAction[] {
  return [
    ...Object.entries(PERIOD_WORDS).map(([value, word]) => ({
      label: `Over the last ${word}`,
      cmd: `${lineFor('usage', ['--by', by, '--days', value])} · reads only`,
      disabledReason: value === days ? 'shown now' : undefined,
      run: () => ctx.period(value),
    })),
    ...['project', 'day', 'model', 'agent'].map((grouping) => ({
      label: `By ${grouping}`,
      cmd: `${lineFor('usage', ['--by', grouping, '--days', days])} · reads only`,
      disabledReason: grouping === by ? 'shown now' : undefined,
      run: () => ctx.group(grouping),
    })),
    { label: 'Work out where spending stands', cmd: lineFor('spend refresh', []), disabledReason: ctx.refused, run: () => ctx.act('spend refresh', []) },
  ];
}
