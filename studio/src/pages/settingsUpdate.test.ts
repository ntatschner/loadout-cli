// Settings > The daemon: the version and updates block. It shows what the
// daemon's daily look found, asks each time whether to wait for the runs or
// restart now, installs and rolls back by running 'loadout update' as a job,
// and shows an update that is waiting for the restart.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import type { UpdateState } from '../lib/api';

const ran: { path: string; words: string[] }[] = [];
const looked: boolean[] = [];
let state: UpdateState;

const NEWER: UpdateState = {
  current: '0.69.0',
  available: '0.70.0',
  isNewer: true,
  notes: 'Studio can update the daemon.',
  checkedAt: '2026-10-22T09:00:00Z',
  problem: null,
  channel: 'archive',
  canRollBack: true,
  automatic: true,
  runs: 2,
  hosted: 1,
  pending: null,
};

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();

  return {
    ...real,
    update: vi.fn(async (check = false) => {
      looked.push(check);

      return state;
    }),
    readJson: vi.fn(async () => ({ enabled: false })),
    teamApi: { ...real.teamApi, daemon: vi.fn(async () => ({ daemon: true, paused: false })) },
    commands: {
      ...real.commands,
      catalogue: vi.fn(async () => []),
      jobs: vi.fn(async () => []),
      run: vi.fn(async (path: string, words: string[]) => {
        ran.push({ path, words });

        return { id: 'j1', state: 'running' };
      }),
    },
  };
});

const { default: Settings } = await import('./Settings.svelte');
const { studio } = await import('../lib/state.svelte');

describe('Settings, the version and updates', () => {
  beforeEach(() => {
    ran.length = 0;
    looked.length = 0;
    state = { ...NEWER };
    studio.update = null;
  });

  afterEach(() => vi.clearAllMocks());

  it('shows what is available, its notes, and how this copy is updated', async () => {
    render(Settings, { rest: ['daemon'] });

    expect(await screen.findByText('0.70.0')).toBeTruthy();
    expect(screen.getByText('What is new in 0.70.0')).toBeTruthy();
    expect(screen.getByText(/unpacked from a release archive/)).toBeTruthy();
    expect(screen.getByRole('button', { name: /Install and restart/ })).toBeTruthy();
    expect(screen.getByRole('button', { name: /Roll back/ })).toBeTruthy();
  });

  it('asks whether to wait, says what restarting now ends, and waits by running update --restart-daemon', async () => {
    render(Settings, { rest: ['daemon'] });

    await fireEvent.click(await screen.findByRole('button', { name: /Install and restart/ }));

    expect(screen.getByText(/the 2 runs it started and the 1 session it hosts/)).toBeTruthy();

    await fireEvent.click(screen.getByRole('button', { name: /Restart when its runs finish/ }));

    expect(ran).toEqual([{ path: 'update', words: ['--restart-daemon', '--yes'] }]);
  });

  it('restarts now only when that is the choice made', async () => {
    render(Settings, { rest: ['daemon'] });

    await fireEvent.click(await screen.findByRole('button', { name: /Install and restart/ }));
    await fireEvent.click(screen.getByRole('button', { name: /Restart now/ }));

    expect(ran).toEqual([{ path: 'update', words: ['--restart-daemon', '--yes', '--now'] }]);
  });

  it('rolls back through the same command, with --roll-back', async () => {
    render(Settings, { rest: ['daemon'] });

    await fireEvent.click(await screen.findByRole('button', { name: /Roll back/ }));
    await fireEvent.click(screen.getByRole('button', { name: /Restart when its runs finish/ }));

    expect(ran).toEqual([{ path: 'update', words: ['--roll-back', '--restart-daemon', '--yes'] }]);
  });

  it('offers no roll back for a copy the MSI installed', async () => {
    state = { ...NEWER, channel: 'installer', canRollBack: false };

    render(Settings, { rest: ['daemon'] });

    expect(await screen.findByText(/installed by the MSI/)).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Roll back/ })).toBeNull();
  });

  it('shows an update waiting for the restart, and offers no second one', async () => {
    state = { ...NEWER, pending: { kind: 'install', version: '0.70.0', now: false, described: 'updating to 0.70.0' } };

    render(Settings, { rest: ['daemon'] });

    expect(await screen.findByText(/Update pending: updating to 0.70.0/)).toBeTruthy();
    expect(screen.getByText(/the 2 runs it started have finished, and starts nothing new meanwhile/)).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Install and restart/ })).toBeNull();
  });

  it('checks now by asking the daemon to read the feed', async () => {
    render(Settings, { rest: ['daemon'] });

    await fireEvent.click(await screen.findByRole('button', { name: /Check now/ }));

    expect(looked).toContain(true);
  });
});
