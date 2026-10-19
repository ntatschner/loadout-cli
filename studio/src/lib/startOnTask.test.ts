// Starting a session on a task: the launch first, then the move, and the
// move only for work not yet under way. A launch that fails moves nothing,
// so a refusal is never recorded as work being done.
import { beforeEach, describe, expect, it, vi } from 'vitest';

const steps: string[] = [];
let refuse = false;

vi.mock('./api', async (original) => ({
  ...(await original<typeof import('./api')>()),
  sessions: {
    list: vi.fn(async () => []),
    start: vi.fn(async (launch: { project: string; taskId?: string }) => {
      steps.push(`launch ${launch.project} ${launch.taskId}`);

      if (refuse) {
        throw new Error('The task is dropped.');
      }

      return { id: 's1' };
    }),
  },
  commands: {
    jobs: vi.fn(async () => []),
    run: vi.fn(async (path: string, words: string[]) => {
      steps.push(`${path} ${words.join(' ')}`);

      return { id: 'j1', state: 'running' };
    }),
  },
}));

const { studio } = await import('./state.svelte');

describe('startOnTask', () => {
  beforeEach(() => {
    steps.length = 0;
    refuse = false;
  });

  it.each(['open', 'blocked'])('launches, then moves a task that is %s to doing', async (state) => {
    const started = await studio.startOnTask({ project: 'p', taskId: 't1' }, state, '$ loadout launch p --task-id t1');

    expect(started).toEqual({ id: 's1' });
    expect(steps).toEqual(['launch p t1', 'task declare t1 doing --project p']);
  });

  it.each(['doing', 'done'])('launches another session on a task that is %s and leaves its state', async (state) => {
    await studio.startOnTask({ project: 'p', taskId: 't1' }, state, '$ loadout launch p --task-id t1');

    expect(steps).toEqual(['launch p t1']);
  });

  it('leaves the task alone when the launch fails', async () => {
    refuse = true;

    const started = await studio.startOnTask({ project: 'p', taskId: 't1' }, 'open', '$ loadout launch p --task-id t1');

    expect(started).toBeNull();
    expect(steps).toEqual(['launch p t1']);
  });
});
