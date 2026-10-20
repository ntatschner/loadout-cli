// Every task across projects on Projects: read through 'task list
// --every-list' a page at a time, moved between pages with the pager, and
// back to the first page when the one being read has gone.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import type { Task } from '../lib/tasks';

const listed: string[][] = [];
let pages = 3;
let gone = 0;

function page(n: number): Task[] {
  return [{ id: `task-${n}`, title: `On page ${n}`, state: 'open', project: n % 2 ? 'website' : null }];
}

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();

  return {
    ...real,
    readJson: vi.fn(async (path: string, words: string[] = []) => {
      if (path !== 'task list') {
        // The page's other reads, as empty as the commands would give them.
        return path === 'project list' ? { projects: [], recorded: 0 } : path === 'idea list' ? { ideas: [] } : path === 'drift' ? [] : {};
      }

      listed.push(words);
      const n = Number(words[words.indexOf('--page') + 1] ?? 1);

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
  });

  it('reads the first page of every list, and the pager moves through the rest', async () => {
    render(Projects, { rest: ['+every'], onlaunch: () => undefined });

    expect(await screen.findByRole('article', { name: 'On page 1, open' })).toBeTruthy();
    expect(listed.at(-1)).toEqual(['--every-list', '--limit', '50', '--page', '1']);
    expect(screen.getByText('Page 1 of 3, 120 in all.')).toBeTruthy();

    await fireEvent.click(screen.getByRole('button', { name: 'Next page' }));

    expect(await screen.findByRole('article', { name: 'On page 2, open' })).toBeTruthy();
    expect(listed.at(-1)).toEqual(['--every-list', '--limit', '50', '--page', '2']);
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
