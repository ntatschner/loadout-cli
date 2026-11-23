// The call sites: a job that fails after the page let go of it says why from
// what it wrote, and an action that sets something to a value offers Retry
// while one that cannot safely run twice does not.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Job } from './api';

let jobs: Job[] = [];
let readOne: Job | null = null;
let actFails = 0;

vi.mock('./api', async (original) => {
  const real = await original<typeof import('./api')>();

  return {
    ...real,
    commands: {
      ...real.commands,
      jobs: vi.fn(async () => jobs),
      job: vi.fn(async () => {
        if (!readOne) {
          throw new TypeError('Failed to fetch');
        }

        return readOne;
      }),
    },
    runs: {
      ...real.runs,
      act: vi.fn(async () => {
        if (actFails-- > 0) {
          throw new real.Refused('The run is not there.', 404, '{"error":"The run is not there."}');
        }
      }),
      list: vi.fn(async () => []),
    },
  };
});

const { studio } = await import('./state.svelte');
const { toasts } = await import('./toasts.svelte');

function job(over: Partial<Job>): Job {
  return {
    id: 'j1',
    path: 'update',
    line: 'loadout update --check',
    state: 'running',
    exitCode: null,
    startedAt: '2026-11-23T01:00:00Z',
    endedAt: null,
    truncated: false,
    output: null,
    error: null,
    ...over,
  };
}

beforeEach(() => {
  toasts.list = [];
  toasts.history = [];
  studio.about = { isDaemon: true } as typeof studio.about;
  studio.jobs = [];
  actFails = 0;
  readOne = null;
});

describe('a job that fails after its page let go', () => {
  it('says why from what it wrote, with its exit code', async () => {
    jobs = [job({})];
    await studio.loadJobs();

    jobs = [job({ state: 'failed', exitCode: 3 })];
    readOne = job({ state: 'failed', exitCode: 3, error: 'Checking the feed\nError: the release feed answered 503\n' });
    await studio.loadJobs();

    expect(toasts.list[0].level).toBe('problem');
    expect(toasts.list[0].text).toBe('Failed: update. Error: the release feed answered 503');
    expect(toasts.list[0].failure?.exitCode).toBe(3);
    expect(toasts.list[0].failure?.output).toContain('Checking the feed');
  });

  it('still gives the exit code when its output cannot be read', async () => {
    jobs = [job({})];
    await studio.loadJobs();

    jobs = [job({ state: 'failed', exitCode: 3 })];
    await studio.loadJobs();

    expect(toasts.list[0].text).toBe("Failed: update. 'loadout update' ended with exit code 3.");
  });
});

describe('Retry on an action', () => {
  const line = { ref: 'r1', where: 'r1' } as Parameters<typeof studio.hold>[0];

  it('is offered where the action sets a value, and works on a later attempt', async () => {
    actFails = 1;

    expect(await studio.hold(line)).toBe(false);
    expect(toasts.list[0].text).toBe('The run is not there.');
    expect(toasts.list[0].failure?.status).toBe(404);
    expect(toasts.list[0].retry).toBeTypeOf('function');

    await toasts.retry(toasts.list[0].id);
    expect(toasts.list[0].text).toBe('Worked on attempt 2');
  });

  it('is not offered where running the action twice could do harm', async () => {
    actFails = 1;

    await studio.stopRun(line);

    expect(toasts.list[0].level).toBe('problem');
    expect(toasts.list[0].retry).toBeUndefined();
  });
});
