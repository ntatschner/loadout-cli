// One task's address over the Projects page: written for each kind of place,
// and read back to the same list and id.
import { describe, expect, it } from 'vitest';
import { taskAt, taskParts } from './tasks';

describe('a task s address on Projects', () => {
  it('names the task after a project, the workspace-wide list or the board of every list', () => {
    expect(taskParts('website', { project: 'website', id: 'fix-login' })).toEqual(['website', 'task', 'fix-login']);
    expect(taskParts('+tasks', { project: null, id: 'loose-end' })).toEqual(['+tasks', 'task', 'loose-end']);
    expect(taskParts('+every', { project: 'homelab', id: 'fix-login' })).toEqual(['+every', 'task', 'homelab', 'fix-login']);
    expect(taskParts('+every', { project: null, id: 'loose-end' })).toEqual(['+every', 'task', '~', 'loose-end']);
  });

  it('reads each back to the list and id it was written for', () => {
    for (const [place, task] of [
      ['website', { project: 'website', id: 'fix-login' }],
      ['+tasks', { project: null, id: 'loose-end' }],
      ['+every', { project: 'homelab', id: 'fix-login' }],
      ['+every', { project: null, id: 'loose-end' }],
    ] as const) {
      expect(taskAt(taskParts(place, task))).toEqual(task);
    }
  });

  it('opens nothing for an address that names no task, or names one by halves', () => {
    expect(taskAt(['website'])).toBeNull();
    expect(taskAt(['+every'])).toBeNull();
    expect(taskAt(['website', 'task'])).toBeNull();
    expect(taskAt(['+every', 'task', 'homelab'])).toBeNull();
  });
});
