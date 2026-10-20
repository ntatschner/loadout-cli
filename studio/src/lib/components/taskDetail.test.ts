// A board over every list, and one task opened in full: each card's moves
// are on its own list, a shared id is two cards, and Details reads
// 'task show' for the list the task is on.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import TaskBoard from './TaskBoard.svelte';
import type { Task, TaskShown } from '../tasks';

const reads: [string, string[]][] = [];
let answer: TaskShown | Error;

vi.mock('../api', async (original) => {
  const real = await original<typeof import('../api')>();

  return {
    ...real,
    readJson: vi.fn(async (path: string, words: string[]) => {
      reads.push([path, words]);

      if (answer instanceof Error) {
        throw answer;
      }

      // As 'task show --json' writes it: the task in the routes' shape, the
      // list's revision, and the idea beside them.
      const { idea, declaredBy, ...task } = answer;

      return { project: words.includes('--project') ? words[words.indexOf('--project') + 1] : null, revision: 'r1', task: { ...task, by: declaredBy }, idea: idea ?? null };
    }),
  };
});

const MIXED: Task[] = [
  { id: 'fix-login', title: 'Fix the login', state: 'open', project: 'website' },
  { id: 'fix-login', title: 'Fix the NAS login', state: 'open', project: 'homelab', hasDetail: true },
  { id: 'loose-end', title: 'Belongs nowhere yet', state: 'doing', project: null },
];

const card = (title: string) => within(screen.getByRole('article', { name: new RegExp(`^${title},`) }));
const lineOf = (button: HTMLElement) => button.closest('.ls-action')?.querySelector('.ls-cmd')?.textContent;

describe('A board over every list', () => {
  it('keeps two tasks with the same id on different lists apart', () => {
    render(TaskBoard, { tasks: MIXED, where: [], mixed: true });

    expect(screen.getByRole('article', { name: 'Fix the login, open' })).toBeTruthy();
    expect(screen.getByRole('article', { name: 'Fix the NAS login, open' })).toBeTruthy();
  });

  it('names each card s project and moves it on its own list', () => {
    render(TaskBoard, { tasks: MIXED, where: [], mixed: true });

    expect(card('Fix the NAS login').getByText(/^homelab · fix-login/)).toBeTruthy();
    expect(card('Belongs nowhere yet').getByText(/^the workspace-wide list · loose-end/)).toBeTruthy();
    expect(lineOf(card('Fix the NAS login').getByRole('button', { name: 'Mark it doing' }))).toBe('$ loadout task declare fix-login doing --project homelab');
    expect(lineOf(card('Belongs nowhere yet').getByRole('button', { name: 'Mark it done' }))).toBe('$ loadout task declare loose-end done --global');
  });

  it('starts a session in the task s own project, and offers none for the workspace-wide list', () => {
    render(TaskBoard, { tasks: MIXED, where: [], mixed: true, onstart: () => undefined });

    expect(lineOf(card('Fix the NAS login').getByRole('button', { name: 'Start' }))).toBe('$ loadout launch homelab --task-id fix-login');
    expect(card('Belongs nowhere yet').queryByRole('button', { name: 'Start' })).toBeNull();
  });

  it('says Details where there is more than the title to read', () => {
    render(TaskBoard, { tasks: MIXED, where: [], mixed: true });

    expect(card('Fix the NAS login').getByRole('button', { name: 'Details and history' })).toBeTruthy();
    expect(card('Fix the login').getByRole('button', { name: 'History' })).toBeTruthy();
  });
});

describe('One task in full', () => {
  it('reads the task on its own list and shows its detail and history', async () => {
    reads.length = 0;
    answer = {
      id: 'fix-login',
      title: 'Fix the NAS login',
      state: 'doing',
      note: 'Ask about the VPN.',
      detail: 'The NAS rejects the new password after a reboot.',
      history: [
        { state: 'open', by: 'nigel', at: '2026-10-19T09:00:00Z' },
        { state: 'doing', by: 'agent', at: '2026-10-20T10:00:00Z', reason: null },
      ],
    };
    render(TaskBoard, { tasks: MIXED, where: [], mixed: true });

    await fireEvent.click(card('Fix the NAS login').getByRole('button', { name: 'Details and history' }));

    const dialog = within(await screen.findByRole('dialog', { name: 'Fix the NAS login' }));
    expect(await dialog.findByText('The NAS rejects the new password after a reboot.')).toBeTruthy();
    expect(dialog.getByText('Ask about the VPN.')).toBeTruthy();
    expect(dialog.getAllByRole('listitem').map((item) => item.querySelector('.st')?.textContent)).toEqual(['open', 'doing']);
    expect(reads).toEqual([['task show', ['fix-login', '--project', 'homelab']]]);
  });

  it('says a task recorded before there was a history has only its latest claim', async () => {
    answer = { id: 'loose-end', title: 'Belongs nowhere yet', state: 'doing', declaredBy: 'nigel', detail: '', history: [] };
    render(TaskBoard, { tasks: MIXED, where: [], mixed: true });

    await fireEvent.click(card('Belongs nowhere yet').getByRole('button', { name: 'History' }));

    expect(await screen.findByText(/Nothing earlier was kept/)).toBeTruthy();
    expect(reads.at(-1)).toEqual(['task show', ['loose-end', '--global']]);
  });

  it('says why when the task could not be read, and closes', async () => {
    answer = new Error("'fix-login' is not a task of website.");
    render(TaskBoard, { tasks: MIXED, where: [], mixed: true });

    await fireEvent.click(card('Fix the login').getByRole('button', { name: 'History' }));

    expect((await screen.findByRole('alert')).textContent).toContain('is not a task of website');

    await fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Close' }));

    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('links a task accepted from an idea back to the idea', async () => {
    answer = {
      id: 'fix-login', title: 'Fix the NAS login', state: 'open', detail: '', history: [],
      idea: { plan: 'projects/homelab/ideas/fix-login.md', accepted: '2026-10-19T10:00:00Z', by: 'nigel' },
    };
    render(TaskBoard, { tasks: MIXED, where: [], mixed: true });

    await fireEvent.click(card('Fix the NAS login').getByRole('button', { name: 'Details and history' }));

    const link = await within(await screen.findByRole('dialog')).findByRole('link', { name: 'Open the idea' });
    expect(link.getAttribute('href')).toBe('#ideas/homelab/fix-login');
    expect(screen.getByText('projects/homelab/ideas/fix-login.md')).toBeTruthy();
  });

  it('names no idea for a task that never was one', async () => {
    answer = { id: 'fix-login', title: 'Fix the login', state: 'open', detail: '', history: [], idea: null };
    render(TaskBoard, { tasks: MIXED, where: [], mixed: true });

    await fireEvent.click(card('Fix the login').getByRole('button', { name: 'History' }));
    await within(await screen.findByRole('dialog')).findByRole('heading', { name: 'History' });

    expect(screen.queryByRole('link', { name: 'Open the idea' })).toBeNull();
  });

  it('reads a one-list board s task on that list', async () => {
    reads.length = 0;
    answer = { id: 't', title: 'One', state: 'open', detail: '', history: [] };
    render(TaskBoard, { tasks: [{ id: 't', title: 'One', state: 'open' }], where: ['--project', 'p'] });

    await fireEvent.click(card('One').getByRole('button', { name: 'History' }));
    await screen.findByRole('dialog');

    expect(reads).toEqual([['task show', ['t', '--project', 'p']]]);
  });
});
