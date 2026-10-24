// A run whose driving process has gone: the page says so, offers End run in
// place of the controls nothing would read, asks first where it cannot be
// sure the process has gone, and offers Forget once the run reads as ended.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';

const sent: { run: string; verb: string; body: Record<string, unknown> }[] = [];

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();

  return {
    ...real,
    runs: {
      list: vi.fn(() => new Promise(() => undefined)),
      act: vi.fn(async (run: string, verb: string, body: Record<string, unknown> = {}) => {
        sent.push({ run, verb, body });

        return 'Done.';
      }),
    },
    teamApi: {
      ...real.teamApi,
      outcomes: vi.fn(async () => []),
      journal: vi.fn(() => () => undefined),
      attachState: vi.fn(async () => ({ here: true, set: false, can: false })),
    },
  };
});

const { studio } = await import('../lib/state.svelte');
const { default: TeamRun } = await import('./TeamRun.svelte');
type Run = import('../lib/api').Run;

function runOf(over: Partial<Run>): Run {
  return {
    id: 'r1',
    team: 'iterating-project',
    goal: 'Clear the dead runs',
    running: true,
    waitingForYou: false,
    ended: null,
    outcome: null,
    project: 'loadout-cli',
    started: '2026-10-23T17:40:00Z',
    finished: null,
    rounds: 2,
    roundLimit: 6,
    elapsedSeconds: 600,
    coverage: [],
    gates: [],
    budget: { spent: 1, cap: null, uncapped: true },
    nodes: [],
    attention: [],
    ...over,
  };
}

const button = (name: string) => screen.queryByRole('button', { name });

describe('TeamRun liveness', () => {
  it('shows not responding badge with pid', async () => {
    studio.runs = [runOf({ liveness: 'not-responding', owner: { pid: 4242, machine: 'here', startedAt: '2026-10-23T17:40:00Z' } })];
    render(TeamRun, { id: 'r1' });
    await tick();

    expect(screen.getByText('not responding · pid 4242')).toBeTruthy();
  });

  it('end run replaces stop on a not responding run', async () => {
    sent.length = 0;
    studio.runs = [runOf({ liveness: 'not-responding', owner: { pid: 4242, machine: 'here', startedAt: '2026-10-23T17:40:00Z' } })];
    render(TeamRun, { id: 'r1' });
    await tick();

    expect(button('Stop after this round')).toBeNull();
    expect(button('Hold after this round')).toBeNull();
    expect(button('Let it carry on')).toBeNull();

    await fireEvent.click(button('End run')!);

    await vi.waitFor(() => expect(sent).toEqual([{ run: 'r1', verb: 'close', body: { confirmed: false } }]));
  });

  it('possibly not responding asks before ending', async () => {
    sent.length = 0;
    studio.runs = [runOf({ liveness: 'possibly-not-responding' })];
    render(TeamRun, { id: 'r1' });
    await tick();

    expect(screen.getByText('possibly not responding')).toBeTruthy();
    await fireEvent.click(button('End run')!);
    await tick();

    expect(sent).toEqual([]);
    const dialog = screen.getByRole('alertdialog', { name: 'End r1?' });
    const confirm = [...dialog.querySelectorAll('button')].find((one) => one.textContent?.trim() === 'End run')!;
    await fireEvent.click(confirm);

    await vi.waitFor(() => expect(sent).toEqual([{ run: 'r1', verb: 'close', body: { confirmed: true } }]));
  });

  it('forget shows once the run has finished', async () => {
    studio.runs = [runOf({ liveness: 'not-responding', owner: { pid: 4242, machine: 'here', startedAt: '2026-10-23T17:40:00Z' } })];
    render(TeamRun, { id: 'r1' });
    await tick();

    expect(button('Forget it')).toBeNull();

    studio.runs = [runOf({ running: false, liveness: 'finished', outcome: 'abandoned', ended: 'abandoned', finished: '2026-10-25T09:00:00Z' })];
    await tick();

    expect(button('Forget it')).toBeTruthy();
    expect(button('End run')).toBeNull();
    expect(screen.queryByText(/not responding/)).toBeNull();
  });
});
