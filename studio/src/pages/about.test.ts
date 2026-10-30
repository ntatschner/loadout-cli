// About Loadout: the version with Update now, the feature table, the guides
// and the release timeline, all from the one model the daemon serves. Update
// now runs the job Settings runs and shows its lines as they come.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import type { AboutLoadout, Job, UpdateState } from '../lib/api';

const ran: { path: string; words: string[] }[] = [];
let model: AboutLoadout;
let reads: (Job | Error)[] = [];
// Answers for the About model, one per read, before it falls back to `model`.
let answers: (AboutLoadout | Error)[] = [];

const UPDATE: UpdateState = {
  current: '0.110.0',
  available: '0.111.0',
  isNewer: true,
  notes: null,
  checkedAt: '2026-10-30T09:00:00Z',
  problem: null,
  channel: 'archive',
  canRollBack: false,
  automatic: true,
  runs: 0,
  hosted: 0,
  pending: null,
};

const MODEL: AboutLoadout = {
  version: '0.110.0',
  devBuild: false,
  available: '0.111.0',
  updateAvailable: true,
  features: [{ id: 'teams', name: 'Team runs', since: '0.30.0', status: 'stable', guide: 'teams', installed: true }],
  guides: [
    { key: 'teams', title: 'Teams guide', url: 'https://example.test/sentinel/teams.md' },
    { key: 'plain', title: 'Plain guide', url: 'http://example.test/plain.md' },
  ],
  timeline: {
    releases: [
      { version: '0.111.0', name: 'Loadout 0.111.0', published: '2026-10-30T08:00:00Z', notes: 'First line\nSecond line', url: 'https://github.com/x/y/releases/tag/v0.111.0', installed: false, kind: 'feature' },
      { version: '0.110.0', name: 'Loadout 0.110.0', published: '2026-10-29T08:00:00Z', notes: 'Older', url: 'javascript:alert(1)', installed: true, kind: 'feature' },
    ],
    empty: null,
    fetchedAt: '2026-10-30T09:00:00Z',
    stale: false,
  },
};

const job = (state: Job['state'], output: string): Job => ({
  id: 'j1', path: 'update', line: 'loadout update', state, exitCode: state === 'done' ? 0 : null,
  startedAt: '', endedAt: null, truncated: false, output, error: null,
});

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();

  return {
    ...real,
    aboutLoadout: vi.fn(async () => {
      const next = answers.shift() ?? model;

      if (next instanceof Error) {
        throw next;
      }

      return next;
    }),
    update: vi.fn(async () => UPDATE),
    commands: {
      ...real.commands,
      catalogue: vi.fn(async () => []),
      jobs: vi.fn(async () => []),
      run: vi.fn(async (path: string, words: string[]) => {
        ran.push({ path, words });

        return job('running', '');
      }),
      // The last read stays the answer, so a job left running stays running
      // until the next test empties the list and it ends.
      job: vi.fn(async () => {
        const next = reads.length > 1 ? reads.shift()! : (reads[0] ?? job('done', ''));

        if (next instanceof Error) {
          throw next;
        }

        return next;
      }),
    },
  };
});

const { default: About } = await import('./About.svelte');
const { studio } = await import('../lib/state.svelte');
const { toasts } = await import('../lib/toasts.svelte');

describe('About Loadout', () => {
  beforeEach(() => {
    ran.length = 0;
    model = structuredClone(MODEL);
    reads = [];
    answers = [];
    toasts.list = [];
    studio.update = null;
  });

  afterEach(async () => {
    // A job a test left polling reads 'done' and stops, rather than taking
    // the next test's reads.
    reads = [];
    await new Promise((resolve) => setTimeout(resolve, 500));
    vi.clearAllMocks();
  });

  it('shows the four sections', async () => {
    render(About);

    expect(await screen.findByRole('heading', { name: 'Version' })).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'What it can do' })).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Guides' })).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Releases' })).toBeTruthy();
    expect(screen.getByText('Team runs')).toBeTruthy();
  });

  it('Update now starts the update job with the Settings words', async () => {
    render(About);

    await fireEvent.click(await screen.findByRole('button', { name: /Update now/ }));
    await fireEvent.click(screen.getByRole('button', { name: /Restart when its runs finish/ }));

    await waitFor(() => expect(ran).toEqual([{ path: 'update', words: ['--restart-daemon', '--yes'] }]));
  });

  it('shows job output lines as progress', async () => {
    reads = [job('running', 'Installed 0.110.0'), job('running', 'Installed 0.110.0\nDownloading 0.111.0'), job('done', 'Installed 0.110.0\nDownloading 0.111.0\nUpdated to 0.111.0')];
    render(About);

    await fireEvent.click(await screen.findByRole('button', { name: /Update now/ }));
    await fireEvent.click(screen.getByRole('button', { name: /Restart when its runs finish/ }));

    const log = await screen.findByRole('log');
    expect(await within(log).findByText('Downloading 0.111.0', {}, { timeout: 3000 })).toBeTruthy();
    expect(await within(log).findByText('Updated to 0.111.0', {}, { timeout: 3000 })).toBeTruthy();
    expect(within(log).getByText('Installed 0.110.0')).toBeTruthy();
  });

  it('shows the lines while the job is still running', async () => {
    reads = [job('running', 'Installed 0.110.0\nDownloading 0.111.0')];
    render(About);

    await fireEvent.click(await screen.findByRole('button', { name: /Update now/ }));
    await fireEvent.click(screen.getByRole('button', { name: /Restart when its runs finish/ }));

    expect(await within(await screen.findByRole('log', {}, { timeout: 3000 })).findByText('Downloading 0.111.0')).toBeTruthy();
  });

  it('newer release marked not installed', async () => {
    render(About);

    const newer = (await screen.findByText('0.111.0', { selector: '.rv b' })).closest('li')!;
    const installed = screen.getByText('0.110.0', { selector: '.rv b' }).closest('li')!;

    expect(within(newer).getByText('not installed')).toBeTruthy();
    expect(within(installed).queryByText('not installed')).toBeNull();
  });

  it('off shows the reason and no timeline', async () => {
    model.timeline = { releases: [], empty: 'Off', emptyText: 'Update checking is switched off on this machine.', fetchedAt: null, stale: false };
    render(About);

    expect(await screen.findByText('Update checking is switched off on this machine.')).toBeTruthy();
    expect(document.querySelector('.timeline')).toBeNull();
  });

  it("guide links are the model's", async () => {
    render(About);

    const link = await screen.findByRole('link', { name: 'Teams guide' });

    expect(link.getAttribute('href')).toBe('https://example.test/sentinel/teams.md');
  });

  it('a link that is not https is not rendered as a link', async () => {
    render(About);

    await screen.findByText('Plain guide');

    expect(screen.queryByRole('link', { name: 'Plain guide' })).toBeNull();
    expect(document.querySelector('a[href^="javascript:"]')).toBeNull();
    expect(document.querySelector('a[href^="http:"]')).toBeNull();
  });

  it('losing the daemon once the update is running is its restart, and the page comes back on the new version', async () => {
    const after = structuredClone(MODEL);
    after.version = '0.111.0';
    after.updateAvailable = false;
    answers = [structuredClone(MODEL), new TypeError('Failed to fetch'), after];
    reads = [job('running', 'Downloading 0.111.0'), new TypeError('Failed to fetch')];
    render(About);

    await fireEvent.click(await screen.findByRole('button', { name: /Update now/ }));
    await fireEvent.click(screen.getByRole('button', { name: /Restart when its runs finish/ }));

    expect(await screen.findByText('Restarting Loadout…', {}, { timeout: 3000 })).toBeTruthy();
    expect(await screen.findByText(', the newest release.', {}, { timeout: 5000 })).toBeTruthy();
    expect(document.querySelector('.vline b')?.textContent).toBe('0.111.0');
    await waitFor(() => expect(screen.queryByText('Restarting Loadout…')).toBeNull());
    expect(toasts.list.filter((one) => one.level === 'problem')).toEqual([]);
  });

  it('a job that reports failing before the daemon goes is a failure, not a restart', async () => {
    reads = [{ ...job('failed', ''), exitCode: 1, error: 'The download did not match its checksum.' }];
    render(About);

    await fireEvent.click(await screen.findByRole('button', { name: /Update now/ }));
    await fireEvent.click(screen.getByRole('button', { name: /Restart when its runs finish/ }));

    await waitFor(() => expect(toasts.list.map((one) => [one.level, one.text])).toEqual([['problem', 'The download did not match its checksum.']]), { timeout: 3000 });
    expect(screen.queryByText('Restarting Loadout…')).toBeNull();
    expect(document.querySelector('.vline b')?.textContent).toBe('0.110.0');
  });
});
