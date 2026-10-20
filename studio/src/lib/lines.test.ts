// A hosted session as a line on the boards: what rises as needing you and
// what stays quiet, from what the agent's own hooks said.
import { describe, expect, it } from 'vitest';
import type { Session } from './api';
import { fromSession } from './lines';
import { signalOf, wordFor } from './signals';

const session: Session = {
  id: 'a1b2c3d4',
  title: 'starstats',
  startedAt: '2026-10-17T10:00:00Z',
  ended: false,
  exitCode: null,
  viewers: 1,
  columns: 120,
  rows: 32,
  terminal: '/api/sessions/a1b2c3d4/terminal',
  memory: null,
  project: 'starstats',
  agent: 'claude',
  mode: null,
  task: null,
  taskId: null,
  command: null,
  lastOutputAt: '2026-10-17T10:00:00Z',
};

const now = Date.parse('2026-10-17T11:00:00Z');

describe('fromSession', () => {
  it('keeps a session waiting on its own background work quiet, out of Needs you', () => {
    const line = fromSession({ ...session, background: true }, now);

    expect(line.waiting).toBe(false);
    expect(line.ask).toBeUndefined();
    expect(wordFor(line)).toBe('waiting on background work');
    expect(signalOf(wordFor(line)).family).toBe('rest');
  });

  it('puts a question before background work, and an ended session before both', () => {
    expect(wordFor(fromSession({ ...session, background: true, needs: 'waiting' }, now))).toBe('waiting for you');
    expect(fromSession({ ...session, background: true, ended: true, exitCode: 0 }, now).background).toBe(false);
  });

  it('reads idle once quiet, as before, when the hooks say nothing', () => {
    expect(wordFor(fromSession(session, now))).toBe('idle');
  });
});
