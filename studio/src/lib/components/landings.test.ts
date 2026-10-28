// The branches waiting to land, as the Teams page shows them: each with where
// it came from and how long it has waited, a branch gone from the repository
// said so, and the command it is read from shown either way.
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import Landings, { mergeWords, type LandingRead } from './Landings.svelte';

const now = new Date('2026-10-28T09:00:00Z');

const one = (over: Partial<LandingRead>): LandingRead => ({
  project: 'website',
  branch: 'queue/login',
  state: 'ready',
  team: 'builders',
  run: 'r1',
  node: '',
  task: 'login',
  pullRequest: '',
  why: '',
  created: '2026-10-27T09:00:00Z',
  gone: false,
  ...over,
});

describe('Landings', () => {
  it('names where each came from, how long it has waited, and links its pull request', () => {
    render(Landings, {
      now,
      landings: [
        one({ pullRequest: 'https://github.com/o/website/pull/42' }),
        one({ branch: 'teams/r1/implementer-2', state: 'pending', node: 'implementer-2', created: '2026-10-28T06:00:00Z' }),
      ],
    });

    expect(screen.getByText('website, run r1, task login, waiting 1 day')).toBeTruthy();
    expect(screen.getByText('website, implementer-2 in run r1, task login, waiting 3 hours')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'https://github.com/o/website/pull/42' })).toBeTruthy();
    expect(screen.getByText('$ loadout pr list')).toBeTruthy();
  });

  it('says when a branch has gone, and when nothing is waiting', () => {
    const { unmount } = render(Landings, { now, landings: [one({ state: 'pending', gone: true })] });

    expect(screen.getByText('gone from the repository')).toBeTruthy();
    unmount();

    render(Landings, { now, landings: [] });
    expect(screen.getByText('Nothing is waiting to land.')).toBeTruthy();
  });

  it('offers to open a pending branch, as a draft or for review, and nothing for one that has gone', async () => {
    const acted: [string, string[]][] = [];
    const act = (path: string, words: string[]) => { acted.push([path, words]); };
    const { unmount } = render(Landings, { now, act, landings: [one({ branch: 'teams/r1/implementer-2', state: 'pending' })] });

    await fireEvent.click(screen.getByRole('button', { name: 'Open as a draft' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Open for review' }));
    expect(acted).toEqual([
      ['pr open', ['teams/r1/implementer-2', '--project', 'website', '--draft']],
      ['pr open', ['teams/r1/implementer-2', '--project', 'website']],
    ]);
    unmount();

    render(Landings, { now, act, landings: [one({ state: 'pending', gone: true })] });
    expect(screen.queryByRole('button', { name: 'Open for review' })).toBeNull();
  });

  it('marks a draft ready', async () => {
    const acted: [string, string[]][] = [];
    render(Landings, { now, act: (path: string, words: string[]) => { acted.push([path, words]); }, landings: [one({ state: 'draft', pullRequest: 'https://github.com/o/website/pull/42' })] });

    await fireEvent.click(screen.getByRole('button', { name: 'Mark ready' }));
    expect(acted).toEqual([['pr ready', ['queue/login', '--project', 'website']]]);
  });

  it('merges only with a subject the person writes, and passes the message as they wrote it', async () => {
    const acted: [string, string[]][] = [];
    render(Landings, { now, act: (path: string, words: string[]) => { acted.push([path, words]); }, landings: [one({ pullRequest: 'https://github.com/o/website/pull/42' })] });

    await fireEvent.click(screen.getByRole('button', { name: 'Merge…' }));
    const go = screen.getByRole('button', { name: 'Squash-merge when checks pass' });
    await fireEvent.click(go);
    expect(acted).toEqual([]); // there is no subject yet

    await fireEvent.input(screen.getByLabelText('Subject'), { target: { value: '  Fix the login  ' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Squash-merge when checks pass' }));
    expect(acted).toEqual([['pr merge', ['queue/login', '--project', 'website', '--subject=Fix the login']]]);
  });

  it('offers no merge once one has been asked for, and says it is waiting on the checks', () => {
    render(Landings, { now, act: () => {}, landings: [one({ pullRequest: 'https://github.com/o/website/pull/42', mergeRequested: '2026-10-28T08:00:00Z' })] });

    expect(screen.queryByRole('button', { name: 'Merge…' })).toBeNull();
    expect(screen.getByText('Merges once its required checks pass.')).toBeTruthy();
  });

  it('keeps a body only when one was written, joined so a leading dash is still a value', () => {
    expect(mergeWords({ project: 'website', branch: 'b' }, '-x', '')).toEqual(['b', '--project', 'website', '--subject=-x']);
    expect(mergeWords({ project: 'website', branch: 'b' }, 'Fix', ' Why. ')).toEqual(['b', '--project', 'website', '--subject=Fix', '--body=Why.']);
  });

  it('offers nothing to press when it is given nothing to act with', () => {
    render(Landings, { now, landings: [one({ state: 'pending' })] });

    expect(screen.queryByRole('button')).toBeNull();
  });
});
