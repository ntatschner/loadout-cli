// The Events page: it asks the daemon for what the address says, shows each
// event with where it happened and whether it cleared, puts every change of
// filter back in the address, and marks an event read when it is opened.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { tick } from 'svelte';
import type { FeedPage } from '../lib/api';

const asked: string[] = [];
const marked: string[][] = [];

const PAGE: FeedPage = {
  items: [
    {
      event: { id: 'e-a', at: '2026-10-21T09:00:00Z', level: 'needs', source: 'run', direction: 'to-me', kind: 'asking', title: 'docs asks: may it merge?', project: 'loadout-cli', run: '20261021-0900-ab12' },
      cleared: '2026-10-21T09:04:00Z',
      read: false,
    },
    {
      event: { id: 'e-b', at: '2026-10-21T08:00:00Z', level: 'needs', source: 'session', direction: 'to-me', kind: 'waiting', title: 'fix the build is waiting for you', session: 'abcd1234' },
      cleared: null,
      read: true,
    },
  ],
  matched: 2,
  unread: 1,
  filter: 'level=needs',
  days: 30,
};

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();

  return {
    ...real,
    eventlog: {
      list: vi.fn(async (filter: string) => {
        asked.push(filter);

        return PAGE;
      }),
      unread: vi.fn(async () => ({ unread: 1 })),
      read: vi.fn(async (ids: string[]) => {
        marked.push(ids);

        return { unread: 0 };
      }),
      readAll: vi.fn(async () => ({ unread: 0 })),
    },
  };
});

const { default: Events } = await import('./Events.svelte');
const { router } = await import('../lib/router.svelte');

describe('the Events page', () => {
  beforeEach(() => {
    asked.length = 0;
    marked.length = 0;
  });

  afterEach(() => vi.restoreAllMocks());

  it('asks for what the address says, and shows each event with what became of it', async () => {
    render(Events, { query: 'level=needs' });

    expect(await screen.findByText(/docs asks: may it merge\?/)).toBeTruthy();
    expect(asked).toEqual(['level=needs']);
    expect(screen.getByText('resolved 4m later')).toBeTruthy();
    expect(screen.getByText('still open')).toBeTruthy();
    expect(screen.getAllByText(/^Unread:/)).toHaveLength(1);
  });

  it('puts a change of filter in the address, as the canonical string', async () => {
    const replace = vi.spyOn(router, 'replaceWith').mockImplementation(() => undefined);

    render(Events, { query: 'level=needs' });
    await screen.findByText(/docs asks/);

    await fireEvent.click(screen.getByRole('button', { name: 'Problems' }));
    expect(replace).toHaveBeenLastCalledWith(['events'], 'level=needs%2Cproblem');

    await fireEvent.click(within(screen.getByRole('radiogroup', { name: 'Which way' })).getByRole('radio', { name: 'Both' }));
    expect(replace).toHaveBeenLastCalledWith(['events'], 'level=needs&direction=any');
  });

  it('marks an event read when it is opened, and links to where it happened', async () => {
    render(Events, { query: 'level=needs' });
    await screen.findByText(/docs asks/);

    const links = screen.getAllByRole('link', { name: 'Go to it' });

    expect(links.map((one) => one.getAttribute('href'))).toEqual(['#teams/20261021-0900-ab12', '#session/abcd1234']);

    await fireEvent.click(links[0]);
    await tick();

    expect(marked).toEqual([['e-a']]);
  });

  it('offers to mark read only what is unread', async () => {
    render(Events, { query: 'level=needs' });
    await screen.findByText(/docs asks/);

    expect(screen.getAllByRole('button', { name: 'Mark read' })).toHaveLength(1);
  });
});
