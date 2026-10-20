// Send answers and refine: the answers go together as one batch, and the
// refine starts only once the batch has recorded them. Started as soon as the
// answers' jobs had started, a refine once ran before them and was refused for
// the questions they answered; a batch refused records none of them, and
// starts no refine on half of them.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';
import type { Job } from '../lib/api';

const events: string[] = [];
const job = (id: string, state: Job['state']): Job => ({ id, path: '', line: '', state, exitCode: state === 'done' ? 0 : null, startedAt: '', endedAt: null, truncated: false, output: '', error: '' });
// Each answer's job is still running when it is started, and done the second time it is asked about.
const asked: Record<string, number> = {};
let applies = true;

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
    ideas: {
      ...real.ideas,
      batch: vi.fn(async (list: string, operations: Record<string, string>[]) => {
        events.push(`batch ${list} ${operations.map((one) => `${one.op} ${one.question}=${one.answer}`).join(', ')}`);

        return { applied: applies, results: operations.map((one) => ({ op: one.op, id: one.id, applied: applies, heldBack: false, error: applies ? null : 'Q1 is not one of the questions' })) };
      }),
    },
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

    expect(events.slice(0, 2)).toEqual(['batch p answer Q1=This', 'start idea refine']);
    vi.useRealTimers();
  });

  it('starts no refine when the batch is refused', async () => {
    events.length = 0;
    applies = false;
    render(Ideas, { rest: ['p', 'i1'] });

    await vi.waitFor(() => expect(screen.getAllByRole('radio', { name: 'This' }).length).toBeGreaterThan(0));
    const shown = screen.getAllByRole('radio', { name: 'This' });
    await fireEvent.click(shown[shown.length - 1]);
    const send = screen.getAllByRole('button', { name: 'Send answers and refine' });
    await fireEvent.click(send[send.length - 1]);

    await vi.waitFor(() => expect(events).toContain('batch p answer Q1=This'));
    await settle();

    expect(events).not.toContain('start idea refine');
    applies = true;
  });
});
