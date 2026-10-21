// The six pages as Pure Focus desks: an item with no state carries no
// Signal; G then I, P, C, M, U or O goes to a page's desk while G then H,
// S, T, K or N still scopes Home's list; and each desk offers, for a sample
// item, the actions the brief lists, each with its command.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import WorkDesk from './WorkDesk.svelte';
import type { PureItem } from '../pure';
import {
  contextActions,
  contextGroups,
  ideaActions,
  ideaGroups,
  memoryActions,
  memoryGroups,
  projectActions,
  projectGroups,
  settingsActions,
  settingsGroups,
  spendActions,
  type DeskContext,
} from '../desks';

// The dividers watch their grid's size, which jsdom has no layout to give.
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;

const view = createRawSnippet(() => ({ render: () => '<div>the page</div>' }));

function desk(groups = settingsGroups()) {
  const went: string[] = [];
  const pages: string[] = [];
  render(WorkDesk, { groups, view, actions: () => [], commands: () => [], ongo: (where: string) => went.push(where), onpage: (page: string) => pages.push(page) });

  return { went, pages };
}

const ctx: DeskContext = { act: async () => 'ran', read: async () => 'read', terminal: async () => 'opened', go: () => undefined };
const lines = (actions: { label: string; cmd?: string; disabledReason?: string }[]) => actions.map((one) => [one.label, one.disabledReason ?? one.cmd]);

describe('WorkDesk on a page', () => {
  it('draws no Signal and no word for an item with no state', () => {
    desk();

    const option = screen.getByRole('option', { name: /Studio/ });
    expect(option.querySelector('.ls-sig')).toBeNull();
    expect(option.querySelector('.w')).toBeNull();
  });

  it('goes to a page with G then I, P, C, M, U or O, and still scopes Home with H, S, T, K and N', async () => {
    const { went, pages } = desk();

    for (const key of ['i', 'p', 'c', 'm', 'u', 'o', 'h', 's', 't', 'k', 'n']) {
      await fireEvent.keyDown(document.body, { key: 'g' });
      await fireEvent.keyDown(document.body, { key });
    }

    expect(pages).toEqual(['ideas', 'projects', 'context', 'memory', 'spend', 'settings']);
    expect(went).toEqual(['all', 'sessions', 'runs', 'tasks', 'needs']);
  });
});

describe('the desks', () => {
  it('Ideas: needs you first, and a failed idea offers the round again and clearing the marker', () => {
    const ideas = [
      { id: 'a', title: 'Shaping', project: 'p', stage: 'proposed', unanswered: 0, running: false },
      { id: 'b', title: 'Failed', project: 'p', stage: 'answering', unanswered: 0, running: false, failure: { error: 'timed out' } },
    ];

    expect(ideaGroups(ideas).map((group) => [group.name, group.items.map((one) => one.title)])).toEqual([['Needs you', ['Failed']], ['p', ['Shaping']]]);
    expect(lines(ideaActions(ideas[1], { slug: 'p', isNew: false, name: 'P' }, ctx))).toEqual([
      ['Try the round again', '$ loadout idea refine b --project p'],
      ['Clear the marker', '$ loadout idea dismiss b --project p'],
      ['Accept it onto p', '$ loadout idea accept b --project p --to p'],
      ['Forget this idea', '$ loadout idea remove b --project p'],
      ['Drop a new idea in', 'opens the box · records nothing yet'],
    ]);
    expect(lines(ideaActions(ideas[0], null, ctx)).map((one) => one[0])).toEqual(['Take it one round further', 'Accept it', 'Forget this idea', 'Drop a new idea in']);
  });

  it('Projects: on this machine first; its editor, folder, what can be put right, onboarding and Forget project', () => {
    const here = { id: 'starstats', name: 'StarStats', available: true };
    const away = { id: 'home-lab', name: 'Home lab', available: false };

    expect(projectGroups([away, here]).map((group) => group.name)).toEqual(['On this machine', 'Not on this machine']);
    expect(lines(projectActions(here, 2, { ...ctx, forget: () => undefined }))).toEqual([
      ['Open it in the editor', '$ loadout code starstats'],
      ['Open its folder', '$ loadout project open starstats'],
      ['Put 2 right', '$ loadout drift starstats --fix --yes'],
      ['Onboard it', '$ loadout project onboard starstats · opens a hosted terminal'],
      ['Forget project…', 'opens the dialog · runs nothing yet'],
    ]);
    expect(lines(projectActions(away, 0, { ...ctx, forget: () => undefined })).map((one) => one[0])).toEqual(['Clone it onto this machine', 'Onboard it', 'Forget project…']);
  });

  it('Context: What loads explains it in each of the four modes, and any item can move to another project', () => {
    const groups = contextGroups('loadout-cli', [{ id: 'language.csharp', title: 'C#', kind: 'language' }]);
    const load = groups[0].items[0] as PureItem;

    expect(load.word).toBe('');
    expect(lines(contextActions(load, 'loadout-cli', { ...ctx, another: () => undefined }))).toEqual([
      ['Explain it in implement mode', '$ loadout instructions explain --project loadout-cli --mode implement · reads only'],
      ['Explain it in advise mode', '$ loadout instructions explain --project loadout-cli --mode advise · reads only'],
      ['Explain it in investigate mode', '$ loadout instructions explain --project loadout-cli --mode investigate · reads only'],
      ['Explain it in review mode', '$ loadout instructions explain --project loadout-cli --mode review · reads only'],
      ['The same for another project', 'the next project · changes nothing'],
    ]);
  });

  it('Settings: the daemon offers what fits how it stands; a section changed in its forms says so', () => {
    const facts = { paused: true, autostart: false, unapproved: [] };
    const more = { ...ctx, setting: async () => 'set', signOut: async () => 'out' };

    expect(lines(settingsActions('daemon', facts, more))).toEqual([
      ['Let the schedules fire', '$ loadout daemon resume'],
      ['Restart it', '$ loadout daemon restart'],
      ['Stop it', '$ loadout daemon stop'],
      ['Start it at log-in', '$ loadout daemon autostart enable'],
    ]);
    expect(lines(settingsActions('loadout', facts, more))).toEqual([["Its changes are in its forms", 'each one is changed where it is shown, in the view']]);
    expect(settingsActions('daemon', facts, more).find((one) => one.label === 'Stop it')?.kind).toBe('danger');
  });

  it('Memory: a project topic can be deleted; every topic offers the index, the clean-up, the review and another project', () => {
    const groups = memoryGroups([{ name: 'release-process', scope: 'project' }, { name: 'paths', scope: 'machine' }]);

    expect(groups.map((group) => group.name)).toEqual(['This project', 'This machine only']);
    expect(lines(memoryActions(groups[0].items[0], 'loadout-cli', { ...ctx, another: () => undefined }))).toEqual([
      ['Delete this topic', '$ loadout memory delete release-process loadout-cli --yes'],
      ['Rebuild the index', '$ loadout memory reindex loadout-cli'],
      ['Clean up what the audit found', '$ loadout memory audit loadout-cli --clean --apply'],
      ['Walk the topics nobody has revisited', '$ loadout memory review loadout-cli · opens a hosted terminal'],
      ["Another project's memory", 'the next project · changes nothing'],
    ]);
    expect(memoryActions(groups[1].items[0], 'loadout-cli', { ...ctx, another: () => undefined })[0].label).toBe('Rebuild the index');
  });

  it('Spend: every period and grouping, the current ones shown now, and spend refresh', () => {
    const actions = lines(spendActions('30', 'project', { ...ctx, period: () => undefined, group: () => undefined }));

    expect(actions).toHaveLength(9);
    expect(actions[2]).toEqual(['Over the last 30 days', 'shown now']);
    expect(actions[0]).toEqual(['Over the last day', '$ loadout usage --by project --days 1 · reads only']);
    expect(actions[4]).toEqual(['By project', 'shown now']);
    expect(actions[8]).toEqual(['Work out where spending stands', '$ loadout spend refresh']);
  });
});

describe('WorkDesk dividers', () => {
  const columns = () => document.querySelector<HTMLElement>('.ls-desk')!.style.getPropertyValue('--desk-cols');

  it('has one between the list and the view and one between the view and the actions, and leaves the stylesheet alone until one moves', () => {
    render(WorkDesk, { groups: settingsGroups(), view, actions: () => [], commands: () => [] });

    expect(screen.getByRole('separator', { name: 'Width of the list' })).toBeTruthy();
    expect(screen.getByRole('separator', { name: 'Width of the actions' })).toBeTruthy();
    expect(columns()).toBe('');
  });

  it('sizes the panes from where they were left, keeping the floors, and moves only the panes either side', async () => {
    const onsplit = vi.fn();
    render(WorkDesk, { groups: settingsGroups(), view, actions: () => [], commands: () => [], split: [0.3, 0.2], onsplit });

    expect(columns()).toBe('minmax(250px, 0.3000fr) minmax(0, 0.5000fr) minmax(230px, 0.2000fr)');

    await fireEvent.keyDown(screen.getByRole('separator', { name: 'Width of the list' }), { key: 'ArrowRight' });
    expect(onsplit.mock.lastCall![0][0]).toBeCloseTo(0.32);
    expect(onsplit.mock.lastCall![0][1]).toBe(0.2);

    await fireEvent.keyDown(screen.getByRole('separator', { name: 'Width of the actions' }), { key: 'ArrowLeft' });
    expect(onsplit.mock.lastCall![0][0]).toBe(0.3);
    expect(onsplit.mock.lastCall![0][1]).toBeCloseTo(0.22);
  });
});
