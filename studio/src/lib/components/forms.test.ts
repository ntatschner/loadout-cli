// CommandForm: the line it shows is the line it runs, and what it ran stays
// shown when the page around it reads its data again.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';
import type { Job, StudioCommand } from '../api';

const ran: { path: string; words: string[] }[] = [];

vi.mock('../api', async (original) => {
  const real = await original<typeof import('../api')>();
  const job: Job = {
    id: 'j1',
    path: 'team message',
    line: 'loadout team message r1 --message hello --non-interactive',
    state: 'done',
    exitCode: 0,
    startedAt: '2026-10-17T01:00:00Z',
    endedAt: '2026-10-17T01:00:01Z',
    truncated: false,
    output: 'The lead reads that at the start of its next round.',
    error: '',
  };

  return {
    ...real,
    commands: {
      ...real.commands,
      run: vi.fn(async (path: string, words: string[]) => {
        ran.push({ path, words });

        return job;
      }),
      job: vi.fn(async () => job),
      jobs: vi.fn(async () => [job]),
    },
  };
});

const { default: CommandForm } = await import('./CommandForm.svelte');

const message: StudioCommand = {
  path: 'team message',
  description: 'Say something to a run’s lead.',
  category: 'Teams',
  example: '',
  runnable: true,
  reach: { page: 'teams', effect: 'changes', reason: null, changesOnlyWith: null },
  arguments: [{ name: 'RUN', position: 0, required: false, many: false, description: '' }],
  options: [{ name: '--message', short: null, kind: 'value', valueName: 'TEXT', description: 'What to say.', required: false, global: false, choices: [] }],
};

async function settle() {
  for (let i = 0; i < 5; i++) {
    await tick();
    await Promise.resolve();
  }
}

describe('CommandForm', () => {
  afterEach(() => {
    ran.length = 0;
  });

  it('runs the words its line shows, with the fixed values the page gave it', async () => {
    render(CommandForm, { command: message, preset: { RUN: 'r1' }, fixed: ['RUN'] });

    await fireEvent.input(screen.getByLabelText(/--message/), { target: { value: 'hello' } });
    await settle();

    expect(screen.getByText('$ loadout team message r1 --message hello')).toBeTruthy();
    expect(screen.queryByLabelText(/^run/)).toBeNull();

    await fireEvent.click(screen.getByRole('button', { name: /^run/i }));
    await settle();

    expect(ran).toEqual([{ path: 'team message', words: ['r1', '--message', 'hello'] }]);
  });

  it('keeps what it ran on show when the page passes the same values again', async () => {
    const shown = render(CommandForm, { command: message, preset: { RUN: 'r1' }, fixed: ['RUN'] });

    await fireEvent.input(screen.getByLabelText(/--message/), { target: { value: 'hello' } });
    await fireEvent.click(screen.getByRole('button', { name: /^run/i }));
    await settle();

    expect(screen.getByText(/reads that at the start of its next round/)).toBeTruthy();

    // A page reading its data again builds a new preset with the same values.
    await shown.rerender({ command: message, preset: { RUN: 'r1' }, fixed: ['RUN'] });
    await settle();

    expect(screen.getByText(/reads that at the start of its next round/)).toBeTruthy();
  });

  it('starts again when the page passes different values', async () => {
    const shown = render(CommandForm, { command: message, preset: { RUN: 'r1' }, fixed: ['RUN'] });

    await fireEvent.click(screen.getByRole('button', { name: /^run/i }));
    await settle();
    await shown.rerender({ command: message, preset: { RUN: 'r2' }, fixed: ['RUN'] });
    await settle();

    expect(screen.queryByText(/reads that at the start of its next round/)).toBeNull();
    expect(screen.getByText('$ loadout team message r2')).toBeTruthy();
  });
});
