// The branches waiting to land, as the Teams page shows them: each with where
// it came from and how long it has waited, a branch gone from the repository
// said so, and the command it is read from shown either way.
import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import Landings, { type LandingRead } from './Landings.svelte';

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
});
