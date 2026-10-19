// What the page reads again for each thing the daemon says has changed,
// which is what keeps it current now that tasks are not on a clock.
import { describe, expect, it } from 'vitest';
import { reloadsFor } from './reloads';

describe('what the page reads again', () => {
  it('reads the task lists when the daemon says a list changed', () => {
    expect(reloadsFor(['tasks'])).toEqual(['tasks']);
  });

  it('reads the history with the sessions', () => {
    expect(reloadsFor(['sessions'])).toEqual(['sessions', 'history']);
  });

  it('reads each thing named, once, and nothing it did not name', () => {
    expect(reloadsFor(['runs', 'jobs', 'tasks'])).toEqual(['runs', 'jobs', 'tasks']);
    expect(reloadsFor([])).toEqual([]);
    expect(reloadsFor(['something new'])).toEqual([]);
  });
});
