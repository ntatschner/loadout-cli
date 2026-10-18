// Send answers and refine: the refine starts only once every answer has
// been recorded. Started as soon as the answers' jobs had started, a refine
// once ran before them and was refused for the questions they answered.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';
import type { Job } from '../lib/api';

const events: string[] = [];
const job = (id: string, state: Job['state']): Job => ({ id, path: '', line: '', state, exitCode: state === 'done' ? 0 : null, startedAt: '', endedAt: null, truncated: false, output: '', error: '' });
// Each answer's job is still running when it is started, and done the second time it is asked about.
const asked: Record<string, number> = {};

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();

  return {
    ...real,
    readJson: vi.fn(async (path: string) => {
      if (path === 'idea list') {
        return { ideas: [{ id: 'i1', title: 'An idea', project: 'p', stage: 'answering', state: 'open', unanswered: 1, running: false }] };
      }

      if (path === 'idea show') {
        return { id: 'i1', project: 'p', stage: 'answering', ask: 'Ask', questions: [{ id: 'Q1', question: 'Which?', why: null, options: ['This', 'That'], recommendation: null, answer: null }], plan: null, accepted: null };
      }

      return new Promise(() => undefined);
    }),
    memory: { ...real.memory, projects: vi.fn(async () => []) },
    commands: {
      ...real.commands,
      run: vi.fn(async (path: string) => {
        events.push(`start ${path}`);

        return job(path, 'running');
      }),
      job: vi.fn(async (id: string) => {
        asked[id] = (asked[id] ?? 0) + 1;

        if (asked[id] > 1) {
          events.push(`done ${id}`);

          return job(id, 'done');
        }

        return job(id, 'running');
      }),
      jobs: vi.fn(async () => []),
    },
  };
});

const { default: Ideas } = await import('./Ideas.svelte');

async function settle(rounds = 10) {
  for (let i = 0; i < rounds; i++) {
    await tick();
    await Promise.resolve();
  }
}

describe('Send answers and refine', () => {
  it('starts the refine only after the answer has been recorded', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    render(Ideas, { rest: ['p', 'i1'] });

    await vi.waitFor(() => expect(screen.getByRole('radio', { name: 'This' })).toBeTruthy());
    await fireEvent.click(screen.getByRole('radio', { name: 'This' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Send answers and refine' }));

    await vi.waitFor(async () => {
      await vi.advanceTimersByTimeAsync(500);
      await settle();
      expect(events).toContain('start idea refine');
    }, { timeout: 5000 });

    expect(events.slice(0, 3)).toEqual(['start idea answer', 'done idea answer', 'start idea refine']);
    vi.useRealTimers();
  });
});
