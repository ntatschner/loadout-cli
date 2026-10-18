// TaskBoard: each state offers exactly its moves, each with the line it
// runs, and done and dropped are folded away rather than left out.
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import TaskBoard from './TaskBoard.svelte';
import type { Task } from '../tasks';

const TASKS: Task[] = [
  { id: 't-open', title: 'Open one', state: 'open' },
  { id: 't-doing', title: 'Doing one', state: 'doing' },
  { id: 't-blocked', title: 'Blocked one', state: 'blocked', note: 'Waiting on access' },
  { id: 't-done', title: 'Done one', state: 'done' },
  { id: 't-dropped', title: 'Dropped one', state: 'dropped' },
];

const moves = (title: string) =>
  within(screen.getByRole('article', { name: new RegExp(`^${title}`) }))
    .getAllByRole('button')
    .map((button) => button.textContent);

describe('TaskBoard', () => {
  it('offers each state exactly its moves', () => {
    render(TaskBoard, { tasks: TASKS, where: ['--project', 'p'] });

    expect(moves('Open one')).toEqual(['Start it', 'It is blocked', 'Drop it']);
    expect(moves('Doing one')).toEqual(['Mark it done', 'It is blocked', 'Reopen it']);
    expect(moves('Blocked one')).toEqual(['Start it', 'Drop it']);
    expect(moves('Done one')).toEqual(['Reopen it']);
    expect(moves('Dropped one')).toEqual(['Reopen it']);
  });

  it('folds done and dropped away under a count', () => {
    const shown = render(TaskBoard, { tasks: TASKS, where: ['--project', 'p'] });

    const folded = shown.container.querySelector('details.closed') as HTMLDetailsElement;
    expect(folded.open).toBe(false);
    expect(within(folded).getByText('1 done, 1 dropped')).toBeTruthy();
    expect(within(folded).getByRole('article', { name: /^Done one/ })).toBeTruthy();
    expect(shown.container.querySelectorAll('.cols .task')).toHaveLength(3);
  });

  it('shows the line each move runs, and moves the card when pressed', async () => {
    const declared: [string, string][] = [];
    render(TaskBoard, { tasks: TASKS, where: ['--project', 'p'], ondeclare: (task: Task, state: string) => declared.push([task.id, state]) });

    const start = within(screen.getByRole('article', { name: /^Open one/ })).getByRole('button', { name: 'Start it' });
    expect(start.closest('.ls-action')?.querySelector('.ls-cmd')?.textContent).toBe('$ loadout task declare t-open doing --project p');

    await fireEvent.click(start);

    expect(declared).toEqual([['t-open', 'doing']]);
    expect(screen.getByRole('article', { name: 'Open one, doing' })).toBeTruthy();
  });

  it('lists what moved away apart from the columns, with only the way to where it is now', () => {
    const shown = render(TaskBoard, {
      tasks: TASKS,
      where: ['--project', 'p'],
      away: [
        { id: 'gone', title: 'Gone one', to: 'homelab', at: '2026-10-18T10:00:00Z' },
        { id: 'global', title: 'Global one', to: null, at: '2026-10-18T10:00:00Z' },
      ],
    });

    const away = within(screen.getByRole('region', { name: 'Moved away' }));
    expect(away.getByText(/Gone one moved to homelab/)).toBeTruthy();
    expect(away.getAllByRole('link', { name: 'Show it' }).map((a) => a.getAttribute('href'))).toEqual(['#projects/homelab', '#projects/+tasks']);
    expect(away.queryAllByRole('button')).toHaveLength(0);
    expect(shown.container.querySelectorAll('.cols .task')).toHaveLength(3);
    expect(screen.queryByRole('article', { name: /Gone one/ })).toBeNull();
  });

  it('says where a moved task came from on its card', () => {
    render(TaskBoard, {
      tasks: [{ id: 'came', title: 'Came one', state: 'open', moved: { from: null, at: '2026-10-18T10:00:00Z' } }],
      where: ['--project', 'p'],
    });

    expect(within(screen.getByRole('article', { name: /^Came one/ })).getByText(/Moved here from the workspace-wide list/)).toBeTruthy();
  });
});
