// The history on Sessions, and what a daemon restart left: each past
// conversation offered back with what it carries over, the one already open
// in Studio gone to rather than started again, and 'Resume all' saying how
// many agents it starts before it starts any.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import type { Conversation, LostSession } from '../lib/api';

const resumed: unknown[] = [];

function conversation(over: Partial<Conversation>): Conversation {
  return {
    agent: 'claude',
    id: 'id',
    title: 'A conversation',
    project: 'alpha',
    registered: true,
    directory: '/repos/alpha',
    branch: 'main',
    lastActive: new Date().toISOString(),
    launchedFor: null,
    running: 'none',
    ...over,
  };
}

const history: Conversation[] = [
  conversation({
    id: 'one',
    title: 'Fix the importer',
    launchedFor: { launch: 'l', task: 'fix the importer', taskId: null, mode: 'implement', profile: null, worktree: null },
  }),
  conversation({ id: 'two', title: 'Open already', running: 'hosted:a1b2c3d4' }),
  conversation({ id: 'three', title: 'Somewhere else', project: null, registered: false, directory: '/tmp/stray' }),
  conversation({ id: 'four', title: 'With Codex', agent: 'codex' }),
];

const lost: LostSession[] = [
  { id: '0000aaaa', endedAt: '', agentSession: 'one', conversation: history[0] },
  { id: '0000bbbb', endedAt: '', agentSession: 'four', conversation: history[3] },
  { id: '0000cccc', endedAt: '', agentSession: 'gone', conversation: null },
];

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();

  return {
    ...real,
    sessions: { ...real.sessions, list: vi.fn(async () => []) },
    conversations: {
      list: vi.fn(async () => history),
      lost: vi.fn(async () => lost),
      forget: vi.fn(async () => undefined),
      resume: vi.fn(async (resume: unknown) => {
        resumed.push(resume);

        return { started: { id: `s${resumed.length}` } };
      }),
    },
  };
});

const { studio } = await import('../lib/state.svelte');
const { default: Sessions } = await import('./Sessions.svelte');

describe('the history on Sessions', () => {
  beforeEach(() => {
    resumed.length = 0;
    studio.conversations = history;
    studio.lost = lost;
  });

  it('offers each conversation back with what it carries over, and says when nothing does', () => {
    render(Sessions, { onlaunch: () => undefined });

    const list = screen.getByRole('list', { name: 'Past conversations' });

    expect(within(list).getByText('carries over task ‘fix the importer’, mode implement')).toBeTruthy();
    expect(within(list).getAllByText(/nothing carried over: no launch on record/).length).toBe(3);
  });

  it('goes to a conversation already open in Studio rather than starting it again', () => {
    render(Sessions, { onlaunch: () => undefined });

    const row = screen.getByText('Open already').closest('li')!;

    expect(within(row).getByRole('button', { name: /^Open/ })).toBeTruthy();
    expect(within(row).queryByRole('button', { name: /^Resume/ })).toBeNull();
  });

  it('says that resuming a conversation from a folder that is not a project registers it', () => {
    render(Sessions, { onlaunch: () => undefined });

    const row = screen.getByText('Somewhere else').closest('li')!;

    expect(within(row).getByRole('button', { name: /Register and resume/ })).toBeTruthy();
  });

  it('narrows to one agent', async () => {
    render(Sessions, { onlaunch: () => undefined });

    await fireEvent.change(screen.getByLabelText('Agent'), { target: { value: 'codex' } });

    const list = screen.getByRole('list', { name: 'Past conversations' });

    expect(within(list).getByText('With Codex')).toBeTruthy();
    expect(within(list).queryByText('Fix the importer')).toBeNull();
  });

  it('lists what a restart ended, leaving out one that never said its conversation', () => {
    render(Sessions, { onlaunch: () => undefined });

    const banner = screen.getByRole('list', { name: 'Ended when the daemon stopped' });

    expect(within(banner).getAllByRole('listitem').length).toBe(2);
  });

  it('names the count and the agents before resuming them all, and starts nothing until told', async () => {
    render(Sessions, { onlaunch: () => undefined });

    await fireEvent.click(screen.getByRole('button', { name: /Resume all 2/ }));

    expect(screen.getByText(/This starts 2 agents: 1 with claude, 1 with codex\./)).toBeTruthy();
    expect(resumed).toEqual([]);

    await fireEvent.click(screen.getByRole('button', { name: /^Resume 2/ }));

    await vi.waitFor(() => expect(resumed.length).toBe(2));
    expect(resumed).toEqual([
      expect.objectContaining({ agentSession: 'one', lost: '0000aaaa' }),
      expect.objectContaining({ agentSession: 'four', lost: '0000bbbb' }),
    ]);
  });
});
