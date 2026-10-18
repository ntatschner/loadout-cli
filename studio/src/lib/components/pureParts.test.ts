// Pure Focus driven by its keys: moving in the list, opening the view, the
// numbered actions and a danger action's second press, the filter, the
// command bar, G then a letter, F6, and the keys keeping out of the way of
// typing. And the flat look, which stops every loop.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { createRawSnippet, tick } from 'svelte';
import { readFileSync } from 'node:fs';
import WorkDesk from './WorkDesk.svelte';
import { pureGroups, type PureAction, type PureGroup, type PureItem } from '../pure';
import type { Work } from '../lines';
import type { Run } from '../api';

const items: PureItem[] = [
  { id: 's1', ref: 's1', kind: 'session', title: 'fix-tests', meta: 'starstats · claude', word: 'working' },
  { id: 's2', ref: 's2', kind: 'session', title: 'docs', meta: 'loadout · claude', word: 'working' },
  { id: 'run:r1', ref: 'r1', kind: 'team run', title: 'Fix the upload', meta: 'bug-hunt', word: 'running' },
];

const groupsOf = (list: PureItem[]): PureGroup[] => [
  { key: 'sessions', name: 'Sessions', items: list.filter((item) => item.kind === 'session') },
  { key: 'runs', name: 'Team runs', items: list.filter((item) => item.kind === 'team run') },
];

function desk(props: Record<string, unknown> = {}) {
  const ran: string[] = [];
  const made: string[] = [];
  const view = createRawSnippet((item: () => PureItem) => ({
    render: () => `<div class="fakeview"><button type="button">inside ${item().title}</button></div>`,
    setup: () => {
      made.push(item().id);
    },
  }));
  const actions = (item: PureItem): PureAction[] => [
    { label: item.word === 'needs approval' ? 'Go to the question' : 'Type into it', cmd: 'focuses it', run: () => ran.push(`first:${item.id}`) },
    { label: 'Hold after this round', cmd: '$ loadout team pause r1', run: () => ran.push(`hold:${item.id}`) },
    { label: 'Stop after this round', kind: 'danger', cmd: '$ loadout team stop r1', run: () => ran.push(`stop:${item.id}`) },
  ];
  const commands = () => [
    { kind: 'go' as const, label: 'The list: sessions', cmd: 'changes nothing', run: () => ran.push('go:sessions') },
    { kind: 'command' as const, label: 'What the agents have cost', cmd: '$ loadout usage', run: () => ran.push('usage') },
  ];
  const result = render(WorkDesk, { groups: groupsOf(items), view, actions, commands, ...props });
  const list = screen.getByRole('listbox', { name: /Everything live/ });
  const picked = () => list.querySelector('[aria-selected="true"]')?.getAttribute('data-id');
  const key = (key: string, target: Element = list, extra: Record<string, unknown> = {}) => fireEvent.keyDown(target, { key, ...extra });

  list.focus();

  return { ...result, ran, made, list, picked, key };
}

afterEach(() => {
  document.body.innerHTML = '';
});

describe('the list', () => {
  it('moves on J and K, the arrows, Home and End', async () => {
    const { picked, key } = desk();
    expect(picked()).toBe('s1');

    await key('j');
    expect(picked()).toBe('s2');
    await key('ArrowDown');
    expect(picked()).toBe('run:r1');
    await key('k');
    expect(picked()).toBe('s2');
    await key('K');
    expect(picked()).toBe('s1');
    // From the first and the last, so End and Home are not one step by chance.
    await key('End');
    expect(picked()).toBe('run:r1');
    await key('Home');
    expect(picked()).toBe('s1');
  });

  it('opens the picked one in the view on Enter, and Escape goes back to the list', async () => {
    const { list, key, container } = desk();

    await key('Enter');
    await tick();
    expect(container.querySelector('.ls-desk')?.classList.contains('viewing')).toBe(true);
    expect(document.activeElement?.textContent).toBe('inside fix-tests');

    await key('Escape', document.activeElement!);
    expect(container.querySelector('.ls-desk')?.classList.contains('viewing')).toBe(false);
    expect(document.activeElement).toBe(list);
  });

  it('takes the keyboard back to the list when a terminal in the view is left with Ctrl+]', async () => {
    const { list, key, container } = desk();

    await key('Enter');
    await tick();
    // What a terminal given nowhere to go sends, as Ctrl+] leaves it.
    document.activeElement!.dispatchEvent(new CustomEvent('leaveterminal', { bubbles: true }));
    await tick();

    expect(document.activeElement).toBe(list);
    expect(container.querySelector('.ls-desk')?.classList.contains('viewing')).toBe(false);
  });

  it('filters on /, and Escape clears the filter and goes back to the list', async () => {
    const { list, key } = desk();

    await key('/');
    const filter = screen.getByRole('searchbox', { name: 'Filter the list' }) as HTMLInputElement;
    expect(document.activeElement).toBe(filter);

    await fireEvent.input(filter, { target: { value: 'upload' } });
    expect([...list.querySelectorAll('[role=option]')].map((node) => node.getAttribute('data-id'))).toEqual(['run:r1']);

    await key('Escape', filter);
    expect(filter.value).toBe('');
    expect(list.querySelectorAll('[role=option]').length).toBe(3);
    expect(document.activeElement).toBe(list);
  });

  it('scopes on G then a letter', async () => {
    const ongo = vi.fn();
    const { key } = desk({ ongo });

    await key('g');
    await key('t');

    expect(ongo).toHaveBeenCalledWith('runs');
  });
});

describe('the actions', () => {
  it('run on their number', async () => {
    const { ran, key } = desk();

    await key('2');
    await tick();

    expect(ran).toEqual(['hold:s1']);
    expect(screen.getByRole('status', { name: '' }).textContent).toContain('✓');
  });

  it('ask for a danger action’s key twice, and Escape keeps it', async () => {
    const { ran, key } = desk();

    await key('3');
    await tick();
    expect(ran).toEqual([]);
    expect(screen.getByRole('button', { name: /Press 3 again to stop after this round/ })).toBeTruthy();

    await key('Escape');
    await tick();
    expect(screen.queryByRole('button', { name: /Press 3 again/ })).toBeNull();
    await key('3');
    await tick();
    expect(ran).toEqual([]);

    await key('3');
    await tick();
    expect(ran).toEqual(['stop:s1']);
  });

  it('stay armed when the list is read again, and disarm when another thing is picked', async () => {
    const { ran, key, rerender } = desk();

    await key('3');
    await tick();
    // A poll: the same things, read again into new objects.
    await rerender({ groups: groupsOf(items.map((item) => ({ ...item }))) });
    await key('3');
    await tick();
    expect(ran).toEqual(['stop:s1']);

    await key('3');
    await tick();
    await key('j');
    await tick();
    expect(screen.queryByRole('button', { name: /Press 3 again/ })).toBeNull();
  });

  it('change with the thing’s state without remaking its view', async () => {
    const { made, rerender } = desk();
    expect(made).toEqual(['s1']);
    expect(screen.getByRole('button', { name: /Type into it/ })).toBeTruthy();

    await rerender({ groups: groupsOf([{ ...items[0], word: 'needs approval' }, items[1], items[2]]) });

    expect(screen.getByRole('button', { name: /Go to the question/ })).toBeTruthy();
    expect(made).toEqual(['s1']);
  });
});

describe('the key help', () => {
  it('opens on ?, takes the keyboard, and Escape gives it back to the list', async () => {
    const { list, key } = desk();

    await key('?');
    await tick();
    await tick();
    const help = screen.getByRole('dialog', { name: 'Keys in Pure Focus' });
    expect(help.contains(document.activeElement)).toBe(true);

    await key('Escape', document.activeElement!);
    await tick();

    expect(screen.queryByRole('dialog', { name: 'Keys in Pure Focus' })).toBeNull();
    expect(document.activeElement).toBe(list);
  });
});

describe('the command bar', () => {
  it('opens on :, runs a match by the arrows and Enter, and gives the list back', async () => {
    const { ran, list, key } = desk();

    await key(':');
    const bar = screen.getByRole('combobox', { name: 'Command bar' });
    expect(document.activeElement).toBe(bar);
    await tick();
    expect(screen.getByRole('option', { name: /What the agents have cost/ })).toBeTruthy();

    await key('ArrowDown', bar);
    await key('Enter', bar);

    expect(ran).toEqual(['usage']);
    expect(document.activeElement).toBe(list);
  });

  it('lets go on Escape', async () => {
    const { ran, list, key } = desk();

    await key(':');
    const bar = screen.getByRole('combobox', { name: 'Command bar' });
    await key('Escape', bar);

    expect(ran).toEqual([]);
    expect(document.activeElement).toBe(list);
  });
});

describe('the keys', () => {
  it('move between the panes on F6, and back on Shift+F6', async () => {
    const { list, key } = desk();

    await key('F6');
    await tick();
    expect(document.activeElement?.textContent).toBe('inside fix-tests');
    await key('F6', document.activeElement!);
    expect(document.activeElement?.textContent).toContain('Type into it');
    await key('F6', document.activeElement!);
    expect(document.activeElement).toBe(screen.getByRole('combobox', { name: 'Command bar' }));
    await key('F6', document.activeElement!, { shiftKey: true });
    expect(document.activeElement?.textContent).toContain('Type into it');
    await key('F6', document.activeElement!);
    await key('F6', document.activeElement!);
    expect(document.activeElement).toBe(list);
  });

  it('do nothing while typing in a box or a terminal', async () => {
    const { ran, picked, key, container } = desk();
    const terminal = document.createElement('div');
    terminal.className = 'xterm';
    const typing = document.createElement('textarea');
    terminal.appendChild(typing);
    container.appendChild(terminal);

    await key('j', typing);
    await key('2', typing);
    await key('2', screen.getByRole('searchbox', { name: 'Filter the list' }));

    expect(picked()).toBe('s1');
    expect(ran).toEqual([]);
  });

  it('with single-letter shortcuts off, leave only the list’s own keys and Escape', async () => {
    const { ran, picked, key, container } = desk({ keys: false });

    await key('j');
    await key('2');
    await key('/');
    expect(picked()).toBe('s1');
    expect(ran).toEqual([]);

    await key('ArrowDown');
    expect(picked()).toBe('s2');
    await key('Enter');
    expect(container.querySelector('.ls-desk')?.classList.contains('viewing')).toBe(true);
  });
});

describe('the list’s groups', () => {
  const line = (over: Partial<Work>) => ({ kind: 'session', id: 'x', ref: 'x', title: 't', where: 'p', stage: 2, ...over }) as Work;
  const run = (id: string, waitingForYou: boolean) => ({ id, goal: id, team: 't', project: 'p', rounds: 1, running: true, waitingForYou, gates: [], nodes: [], budget: { spent: 0, cap: null, uncapped: false } }) as unknown as Run;

  it('put what needs you first, once, and scope to one kind or to what needs you', () => {
    const lines = [line({ id: 'a', ref: 'a', waiting: true }), line({ id: 'b', ref: 'b' }), line({ kind: 'task', id: 'k', ref: 'k' })];
    const runs = [run('r1', true), run('r2', false)];

    const all = pureGroups(lines, runs, 10);
    expect(all.map((group) => [group.key, group.items.map((item) => item.id)])).toEqual([
      ['needs', ['a', 'run:r1']],
      ['sessions', ['b']],
      ['runs', ['run:r2']],
      ['ideas', []],
      ['tasks', ['k']],
    ]);

    expect(pureGroups(lines, runs, 10, 'sessions').map((group) => group.items.map((item) => item.id))).toEqual([['a', 'b']]);
    expect(pureGroups(lines, runs, 10, 'needs').map((group) => group.items.map((item) => item.id))).toEqual([['a', 'run:r1']]);
  });
});

describe('the flat look', () => {
  it('stops every glyph’s loop under data-mode="pure"', () => {
    const componentsCss = readFileSync('src/styles/components.css', 'utf-8');

    expect(componentsCss).toMatch(/\[data-mode="pure"\] \.ls-sig \.ic[^{]*\{ animation: none !important; \}/);
  });
});
