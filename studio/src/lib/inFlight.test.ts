// The tube map's 'In flight' scope: what stays on the map and what comes off,
// one case per rule, through linesOf so a change there cannot quietly widen
// the map again.
import { describe, expect, it } from 'vitest';
import type { Idea, Run, Session, TaskList } from './api';
import { hiddenOf, inFlight, linesOf } from './lines';

const now = Date.parse('2026-10-26T12:00:00Z');
const hoursAgo = (hours: number) => new Date(now - hours * 3600_000).toISOString();

function run(id: string, over: Partial<Run> = {}): Run {
  return {
    id,
    team: 'build',
    goal: id,
    running: false,
    waitingForYou: false,
    ended: null,
    outcome: null,
    project: 'starstats',
    started: hoursAgo(3),
    finished: null,
    rounds: 1,
    roundLimit: 4,
    elapsedSeconds: 60,
    coverage: [],
    gates: [],
    budget: { spent: 0, cap: null, uncapped: true },
    nodes: [],
    attention: [],
    ...over,
  };
}

function session(id: string, over: Partial<Session> = {}): Session {
  return {
    id,
    title: id,
    startedAt: hoursAgo(2),
    ended: false,
    exitCode: null,
    viewers: 0,
    columns: 120,
    rows: 32,
    terminal: `/api/sessions/${id}/terminal`,
    memory: null,
    project: 'starstats',
    agent: 'claude',
    mode: null,
    task: null,
    taskId: null,
    command: null,
    lastOutputAt: hoursAgo(1),
    ...over,
  };
}

function idea(id: string, stage: Idea['detail']['stage'], over: Partial<Idea['detail']> = {}): Idea {
  return {
    title: id,
    detail: { id, project: 'starstats', stage, captured: hoursAgo(5), error: null, lastRound: null, questions: [], ...over },
  };
}

const tasks = (...states: string[]): TaskList[] =>
  [{ project: 'starstats', tasks: states.map((state) => ({ id: state, title: state, state })) }] as unknown as TaskList[];

const drawn = (input: Partial<Parameters<typeof linesOf>[0]>) =>
  inFlight(linesOf({ runs: [], sessions: [], ideas: [], busy: [], tasks: [], ...input }, now), now).map((line) => line.ref);

describe('inFlight', () => {
  it('leaves every idea off, whatever its stage, a round running and questions waiting included', () => {
    const ideas = [
      idea('captured', 'captured'),
      idea('answering', 'answering', { questions: [{ id: 'q1', question: 'Which?', answer: null }] }),
      idea('ready', 'ready'),
      idea('proposed', 'proposed'),
      idea('failed', 'ready', { error: 'the round failed' }),
      idea('busy', 'ready'),
    ];

    expect(drawn({ ideas, busy: ['busy'] })).toEqual([]);
  });

  it('keeps runs going or waiting for you, and leaves done, stopped and cancelled ones off', () => {
    const runs = [
      run('going', { running: true }),
      run('asking', { running: true, waitingForYou: true }),
      run('done', { outcome: 'done', finished: hoursAgo(1) }),
      run('stopped', { outcome: 'stopped', finished: hoursAgo(1) }),
      run('cancelled', { outcome: 'cancelled', finished: hoursAgo(1) }),
    ];

    expect(drawn({ runs })).toEqual(['going', 'asking']);
  });

  it('keeps a failed run for a day from when it finished', () => {
    const runs = [run('lately', { outcome: 'failed', finished: hoursAgo(23) }), run('yesterday', { outcome: 'failed', finished: hoursAgo(25) })];

    expect(drawn({ runs })).toEqual(['lately']);
  });

  it('keeps live sessions and leaves those that exited with 0, or with no code, off', () => {
    const sessions = [
      session('live'),
      session('clean', { ended: true, exitCode: 0, endedAt: hoursAgo(1) }),
      session('lost', { ended: true, exitCode: null, endedAt: hoursAgo(1) }),
      session('failed', { ended: true, exitCode: 1, endedAt: hoursAgo(1) }),
    ];

    expect(drawn({ sessions })).toEqual(['live', 'failed']);
  });

  it('keeps a session that failed for a day from when it ended, and one with no end time not at all', () => {
    const sessions = [
      session('lately', { ended: true, exitCode: 2, endedAt: hoursAgo(23) }),
      session('yesterday', { ended: true, exitCode: 2, endedAt: hoursAgo(25) }),
      session('older daemon', { ended: true, exitCode: 2 }),
    ];

    expect(drawn({ sessions })).toEqual(['lately']);
  });

  it('keeps tasks being done or blocked, and open and done ones stay off as linesOf leaves them', () => {
    expect(drawn({ tasks: tasks('open', 'doing', 'blocked', 'done') })).toEqual(['doing', 'blocked']);
  });

  it('codes the lines it keeps again in drawing order, so no two of the first eight share one', () => {
    const lines = linesOf({ runs: [run('done', { outcome: 'done', finished: hoursAgo(1) }), run('going', { running: true })], sessions: [session('live')], ideas: [idea('a', 'ready')], busy: [], tasks: [] }, now);

    expect(inFlight(lines, now).map((line) => line.code)).toEqual(['1', '2']);
  });
});

describe('hiddenOf', () => {
  it('says what was left off, by kind', () => {
    const lines = linesOf(
      {
        runs: [run('done', { outcome: 'done', finished: hoursAgo(1) }), run('going', { running: true })],
        sessions: [session('clean', { ended: true, exitCode: 0, endedAt: hoursAgo(1) })],
        ideas: [idea('a', 'ready'), idea('b', 'captured')],
        busy: [],
        tasks: [],
      },
      now,
    );

    expect(hiddenOf(lines, inFlight(lines, now))).toBe('1 team run, 1 session and 2 ideas');
    expect(hiddenOf(lines, lines)).toBe('');
  });
});
