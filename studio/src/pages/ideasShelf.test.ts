// The Ideas shelf kept scannable: dropped ideas folded away under a count,
// the list read with a limit and said so when there is more than it holds,
// and an accepted idea linked to the board its task is on.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/svelte';

const listed: string[][] = [];
let total = 3;

const IDEAS = [
  { id: 'live', title: 'One being shaped', project: 'p', stage: 'answering', state: 'open', unanswered: 1, running: false, type: 'ci-cd' },
  { id: 'other', title: 'Another being shaped', project: null, stage: 'captured', state: 'open', unanswered: 0, running: false },
  { id: 'gone', title: 'One given up on', project: 'p', stage: 'captured', state: 'dropped', unanswered: 0, running: false },
];

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();

  return {
    ...real,
    readJson: vi.fn(async (path: string, words: string[] = []) => {
      if (path === 'idea list') {
        listed.push(words);

        return { ideas: IDEAS, recorded: total, total };
      }

      if (path === 'idea show') {
        return {
          id: 'done-one', project: 'p', stage: 'accepted', ask: 'Ask', questions: [], plan: null,
          accepted: { project: 'website', plan: 'projects/website/ideas/done-one.md', by: 'nigel', when: '2026-10-19T10:00:00Z' },
        };
      }

      return new Promise(() => undefined);
    }),
    memory: { ...real.memory, projects: vi.fn(async () => []) },
    commands: { ...real.commands, jobs: vi.fn(async () => []) },
  };
});

const { default: Ideas } = await import('./Ideas.svelte');

describe('the Ideas shelf', () => {
  beforeEach(() => {
    listed.length = 0;
    total = 3;
  });

  it('folds dropped ideas away under a count, out of the groups', async () => {
    render(Ideas, { rest: [] });

    const shelf = await screen.findByRole('navigation', { name: 'Ideas' });
    const folded = (await within(shelf).findByText('1 dropped')).closest('details') as HTMLDetailsElement;

    expect(folded.open).toBe(false);
    expect(within(folded).getByText('One given up on')).toBeTruthy();
    expect(within(shelf).getAllByText('One given up on')).toHaveLength(1);
    expect(within(shelf).getByText('One being shaped').closest('details')).toBeNull();
  });

  it('reads the shelf with a limit, and says when there are more than it holds', async () => {
    total = 250;
    render(Ideas, { rest: [] });

    expect(await screen.findByText(/The newest 3 are shown and 247 more are not/)).toBeTruthy();
    expect(listed[0]).toEqual(['--limit', '200']);
  });

  it("names each idea's type, and nothing for one recorded before there were types", async () => {
    render(Ideas, { rest: [] });

    const shelf = await screen.findByRole('navigation', { name: 'Ideas' });
    const typed = within(shelf).getByText('One being shaped').closest('a') as HTMLElement;
    const untyped = within(shelf).getByText('Another being shaped').closest('a') as HTMLElement;

    expect(within(typed).getByText('CI/CD')).toBeTruthy();
    expect(untyped.querySelector('.ls-tag.type')).toBeNull();
  });

  it('says nothing about more when the shelf holds them all', async () => {
    render(Ideas, { rest: [] });

    await screen.findByText('One being shaped');

    expect(screen.queryByText(/more are not/)).toBeNull();
  });

  it('links an accepted idea to the board its task is on', async () => {
    render(Ideas, { rest: ['p', 'done-one'] });

    const link = await screen.findByRole('link', { name: "Its task is on website's board" });

    expect(link.getAttribute('href')).toBe('#projects/website');
  });
});
