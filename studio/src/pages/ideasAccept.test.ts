// Accept goes through the plan: pressing it reads 'idea plan' and shows the
// preview, nothing is run until Confirm, Back runs nothing, and Confirm runs
// 'idea accept' held to the hash of the plan that was shown. An address
// ending /+accept, which is how a desk's Accept arrives, opens it too.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import type { Job } from '../lib/api';

const reads: [string, string[]][] = [];
const runs: [string, string[]][] = [];
const job = (path: string): Job => ({ id: 'j1', path, line: '', state: 'running', exitCode: null, startedAt: '', endedAt: null, truncated: false, output: '', error: '' });

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();

  return {
    ...real,
    readJson: vi.fn(async (path: string, words: string[]) => {
      reads.push([path, words]);

      if (path === 'idea list') {
        return { ideas: [{ id: 'i1', title: 'An idea', project: 'p', stage: 'proposed', state: 'open', unanswered: 0, running: false }] };
      }

      if (path === 'idea show') {
        return {
          id: 'i1', project: 'p', stage: 'proposed', ask: 'Ask', questions: [], accepted: null,
          plan: { revision: 2, title: 'Status page', understanding: 'A page.', layers: [], additions: [], doneWhen: ['every host is listed'], placement: { slug: 'p', isNew: false, name: 'P', reason: 'it is about p' } },
        };
      }

      if (path === 'idea plan') {
        return { id: 'i1', project: 'p', state: 'preview', markdown: '# Status page\n\nFrom the idea.\n', hash: 'abc123', undecided: ['Storage'], gapFilled: [] };
      }

      return new Promise(() => undefined);
    }),
    memory: { ...real.memory, projects: vi.fn(async () => [{ slug: 'p', name: 'P' }]) },
    commands: {
      ...real.commands,
      run: vi.fn(async (path: string, words: string[]) => {
        runs.push([path, words]);

        return job(path);
      }),
      jobs: vi.fn(async () => []),
    },
  };
});

const { default: Ideas } = await import('./Ideas.svelte');

const planReads = () => reads.filter(([path]) => path === 'idea plan');
const dialog = () => screen.queryByRole('dialog', { name: 'Read the plan, then confirm' });

async function openAccept() {
  render(Ideas, { rest: ['p', 'i1'] });

  await vi.waitFor(() => expect(screen.getAllByRole('button', { name: 'Accept the plan' }).length).toBeGreaterThan(0));
  const buttons = screen.getAllByRole('button', { name: 'Accept the plan' });
  await fireEvent.click(buttons[buttons.length - 1]);
  await vi.waitFor(() => expect(screen.getByRole('heading', { name: 'Status page', level: 1 })).toBeTruthy());
}

describe('Accept goes through the plan', () => {
  it('shows the preview with its warnings and runs nothing until Confirm', async () => {
    reads.length = 0;
    runs.length = 0;
    await openAccept();

    expect(dialog()).toBeTruthy();
    expect(planReads().at(-1)).toEqual(['idea plan', ['i1', '--project', 'p']]);
    expect(screen.getByText(/Storage goes in with no option chosen/)).toBeTruthy();
    expect(runs).toEqual([]);
  });

  it('Back closes it and accepts nothing', async () => {
    runs.length = 0;
    await openAccept();

    const backs = screen.getAllByRole('button', { name: 'Back' });
    await fireEvent.click(backs[backs.length - 1]);

    expect(dialog()).toBeNull();
    expect(runs).toEqual([]);
  });

  it('Confirm runs idea accept held to the hash of what was shown', async () => {
    runs.length = 0;
    await openAccept();

    const confirms = screen.getAllByRole('button', { name: 'Confirm and accept' });
    await fireEvent.click(confirms[confirms.length - 1]);

    await vi.waitFor(() => expect(runs.length).toBe(1));
    expect(runs[0]).toEqual(['idea accept', ['i1', '--project', 'p', '--to', 'p', '--plan-hash=abc123']]);
  });

  it('opens on arrival at an address ending /+accept', async () => {
    reads.length = 0;
    runs.length = 0;
    render(Ideas, { rest: ['p', 'i1', '+accept'] });

    await vi.waitFor(() => expect(dialog()).toBeTruthy());
    await vi.waitFor(() => expect(planReads().length).toBeGreaterThan(0));
    expect(runs).toEqual([]);
  });
});
