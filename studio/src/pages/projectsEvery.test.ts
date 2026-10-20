// Every task across projects on Projects: read through 'task list
// --every-list' a page at a time, moved between pages with the pager, and
// back to the first page when the one being read has gone.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import type { Task } from '../lib/tasks';

const listed: string[][] = [];
const shown: string[][] = [];
let pages = 3;
let gone = 0;
// A read held back until the test lets it go, to see the page while it waits.
let held = false;
let release: () => void = () => undefined;

function page(n: number): Task[] {
  return [{ id: `task-${n}`, title: `On page ${n}`, state: 'open', project: n % 2 ? 'website' : null }];
}

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();

  return {
    ...real,
    readJson: vi.fn(async (path: string, words: string[] = []) => {
      if (path === 'task show') {
        shown.push(words);

        return { id: words[0], title: 'Belongs nowhere yet', state: 'open', detail: '', history: [] };
      }

      if (path !== 'task list') {
        // The page's other reads, as empty as the commands would give them.
        return path === 'project list' ? { projects: [], recorded: 0 } : path === 'idea list' ? { ideas: [] } : path === 'drift' ? [] : {};
      }

      listed.push(words);
      const n = Number(words[words.indexOf('--page') + 1] ?? 1);

      if (held) {
        held = false;
        await new Promise<void>((resolve) => (release = resolve));
      }

      if (n > pages - gone) {
        throw new real.Refused(`There are ${pages - gone} pages of 50, so there is no page ${n}.`, 400);
      }

      return { tasks: page(n), recorded: 120, total: 120, page: n, pages: pages - gone, query: { text: '', parts: [], fields: [], defaultQuery: '' } };
    }),
  };
});

const { default: Projects } = await import('./Projects.svelte');

describe('every task across projects', () => {
  beforeEach(() => {
    listed.length = 0;
    pages = 3;
    gone = 0;
    held = false;
  });

  it('reads the first page of every list, and the pager moves through the rest', async () => {
    render(Projects, { rest: ['+every'], onlaunch: () => undefined });

    expect(await screen.findByRole('article', { name: 'On page 1, open' })).toBeTruthy();
    expect(listed.at(-1)).toEqual(['--every-list', '--limit', '50', '--page', '1']);
    expect(screen.getByText('Page 1 of 3, 120 in all.')).toBeTruthy();

    await fireEvent.click(screen.getByRole('button', { name: 'Next page' }));

    expect(await screen.findByRole('article', { name: 'On page 2, open' })).toBeTruthy();
    expect(listed.at(-1)).toEqual(['--every-list', '--limit', '50', '--page', '2']);
    expect(screen.getByText('Page 2 of 3, 120 in all.')).toBeTruthy();
  });

  it('names the page only once the board shows it', async () => {
    render(Projects, { rest: ['+every'], onlaunch: () => undefined });

    await screen.findByRole('article', { name: 'On page 1, open' });
    held = true;
    await fireEvent.click(screen.getByRole('button', { name: 'Next page' }));

    // The read of page 2 has not come back: the board and the pager both
    // still say page 1.
    expect(screen.getByRole('article', { name: 'On page 1, open' })).toBeTruthy();
    expect(screen.getByText('Page 1 of 3, 120 in all.')).toBeTruthy();

    release();
    expect(await screen.findByText('Page 2 of 3, 120 in all.')).toBeTruthy();
    expect(screen.getByRole('article', { name: 'On page 2, open' })).toBeTruthy();
  });

  it('says an empty column is empty on this page only, since the rest may be on another', async () => {
    render(Projects, { rest: ['+every'], onlaunch: () => undefined });

    await screen.findByRole('article', { name: 'On page 1, open' });

    expect(screen.getAllByText('None on this page.')).toHaveLength(2);
    expect(screen.queryByText('Nothing under way.')).toBeNull();
  });

  it('opens a task from the address, on whichever list it names', async () => {
    shown.length = 0;
    render(Projects, { rest: ['+every', 'task', '~', 'loose-end'], onlaunch: () => undefined });

    const dialog = await screen.findByRole('dialog');

    expect(dialog).toBeTruthy();
    await vi.waitFor(() => expect(shown).toEqual([['loose-end', '--global']]));
  });

  it('writes the task it opens into the address, without leaving the page', async () => {
    history.replaceState(null, '', '#projects/+every');
    render(Projects, { rest: ['+every'], onlaunch: () => undefined });

    const card = await screen.findByRole('article', { name: 'On page 1, open' });
    await fireEvent.click(within(card).getByRole('button', { name: 'History' }));

    // The router writes '+' as %2B, as it does for every place, and reads it back.
    expect(decodeURIComponent(location.hash)).toBe('#projects/+every/task/website/task-1');
  });

  it('goes back to the first page when the one being read has gone', async () => {
    render(Projects, { rest: ['+every'], onlaunch: () => undefined });

    await screen.findByRole('article', { name: 'On page 1, open' });
    await fireEvent.click(screen.getByRole('button', { name: 'Next page' }));
    await screen.findByRole('article', { name: 'On page 2, open' });

    // Tasks finished elsewhere since page 2 was read: one page now, and the
    // page still offered as next is refused.
    gone = 2;
    await fireEvent.click(screen.getByRole('button', { name: 'Next page' }));

    expect(await screen.findByRole('article', { name: 'On page 1, open' })).toBeTruthy();
    expect(listed.at(-1)).toEqual(['--every-list', '--limit', '50', '--page', '1']);
  });
});
