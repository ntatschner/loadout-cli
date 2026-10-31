// The type picker on the idea workbench: it says whose the type is now, and
// choosing from it runs 'idea type', with 'agent' handing the type back.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';
import type { Job } from '../lib/api';

let type: string | null = 'feature';
let typeBy: string | null = 'rule';
const ran: [string, string[]][] = [];
const job = (id: string): Job => ({ id, path: '', line: '', state: 'done', exitCode: 0, startedAt: '', endedAt: null, truncated: false, output: '', error: '' });

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();

  return {
    ...real,
    readJson: vi.fn(async (path: string) => {
      if (path === 'idea list') {
        return { ideas: [{ id: 'i1', title: 'An idea', project: 'p', stage: 'captured', state: 'open', unanswered: 0, running: false, type }] };
      }

      if (path === 'idea show') {
        return { id: 'i1', project: 'p', stage: 'captured', ask: 'Ask', questions: [], plan: null, accepted: null, type, typeBy };
      }

      return new Promise(() => undefined);
    }),
    memory: { ...real.memory, projects: vi.fn(async () => []) },
    commands: {
      ...real.commands,
      run: vi.fn(async (path: string, words: string[]) => {
        ran.push([path, words]);

        return job(path);
      }),
      job: vi.fn(async (id: string) => job(id)),
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

async function picker(): Promise<HTMLSelectElement> {
  render(Ideas, { rest: ['p', 'i1'] });

  return (await screen.findByLabelText('Type')) as HTMLSelectElement;
}

describe('the type picker', () => {
  beforeEach(() => {
    type = 'feature';
    typeBy = 'rule';
    ran.length = 0;
  });

  it('shows a guess as the agent’s to decide, and says what it was guessed as', async () => {
    const select = await picker();

    expect(select.value).toBe('agent');
    expect(screen.getByText(/guessed as feature; the agent may revise it each round/)).toBeTruthy();
  });

  it('shows a type the person chose as chosen', async () => {
    type = 'bug-fix';
    typeBy = 'person';

    const select = await picker();

    expect(select.value).toBe('bug-fix');
    expect(screen.getByText('your choice; no round changes it')).toBeTruthy();
  });

  it('runs idea type with the type chosen', async () => {
    const select = await picker();

    await fireEvent.change(select, { target: { value: 'release' } });
    await settle();

    expect(ran).toContainEqual(['idea type', ['i1', 'release', '--project', 'p']]);
  });

  it('hands a chosen type back to the agent', async () => {
    type = 'docs';
    typeBy = 'person';

    const select = await picker();

    await fireEvent.change(select, { target: { value: 'agent' } });
    await settle();

    expect(ran).toContainEqual(['idea type', ['i1', 'agent', '--project', 'p']]);
  });

  it('runs nothing when the choice is the one already made', async () => {
    const select = await picker();

    await fireEvent.change(select, { target: { value: 'agent' } });
    await settle();

    expect(ran.filter(([path]) => path === 'idea type')).toEqual([]);
  });
});
