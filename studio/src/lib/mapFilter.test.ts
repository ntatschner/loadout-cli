// Home's filters: the In flight preset, one case per rule, through linesOf so a
// change there cannot quietly widen the map again; then each filter, the
// presets pinned to what the map drew before there were filters, and the
// filters as config holds them.
import { describe, expect, it } from 'vitest';
import type { Idea, Run, Session, TaskList } from './api';
import { hiddenOf, linesOf, type Work } from './lines';
import { NO_PROJECT, PRESETS, filterLines, filterOf, hiddenNote, keysOf, presetOf, projectsIn, stateOf, toggled, type MapFilter } from './mapFilter';

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

const inFlight = (lines: Work[], at = now) => filterLines(lines, PRESETS.inflight, at).drawn;

const drawn = (input: Partial<Parameters<typeof linesOf>[0]>) =>
  inFlight(linesOf({ runs: [], sessions: [], ideas: [], busy: [], tasks: [], ...input }, now), now).map((line) => line.ref);

describe('the In flight preset', () => {
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

// One fixed set with a line for every rule, the same for both presets.
const fixed = () =>
  linesOf(
    {
      runs: [
        run('run going', { running: true }),
        run('run asking', { running: true, waitingForYou: true, gates: [{ id: 'g1', node: 'lead', question: 'Ship it?', at: hoursAgo(1) }] as Run['gates'] }),
        run('run done', { outcome: 'done', finished: hoursAgo(1) }),
        run('run failed lately', { outcome: 'failed', finished: hoursAgo(2) }),
        run('run failed yesterday', { outcome: 'failed', finished: hoursAgo(23.9) }),
        run('run failed long ago', { outcome: 'failed', finished: hoursAgo(30), waitingForYou: true }),
        run('run elsewhere', { running: true, project: null }),
      ],
      sessions: [
        session('session live'),
        session('session approval', { needs: 'approval' }),
        session('session clean', { ended: true, exitCode: 0, endedAt: hoursAgo(1) }),
        session('session failed', { ended: true, exitCode: 1, endedAt: hoursAgo(1) }),
        session('session failed long ago', { ended: true, exitCode: 1, endedAt: hoursAgo(26) }),
        session('session outside', { project: null }),
      ],
      ideas: [
        idea('idea captured', 'captured'),
        idea('idea answering', 'answering', { questions: [{ id: 'q1', question: 'Which?', answer: null }] }),
        idea('idea proposed', 'proposed', { project: 'website' }),
      ],
      busy: [],
      tasks: tasks('open', 'doing', 'blocked', 'done'),
    },
    now,
  );

const refs = (lines: Work[]) => lines.map((line) => line.ref);

describe('the presets, pinned to what the map drew before there were filters', () => {
  it('In flight draws what inFlight drew', () => {
    expect(refs(filterLines(fixed(), PRESETS.inflight, now).drawn)).toEqual([
      'run going',
      'run asking',
      'run failed lately',
      'run failed yesterday',
      'run failed long ago',
      'run elsewhere',
      'session live',
      'session approval',
      'session failed',
      'session outside',
      'doing',
      'blocked',
    ]);
  });

  it('Everything draws every line linesOf gives', () => {
    const lines = fixed();

    expect(refs(filterLines(lines, PRESETS.all, now).drawn)).toEqual(refs(lines));
    expect(refs(lines)).toEqual([
      'run going',
      'run asking',
      'run done',
      'run failed lately',
      'run failed yesterday',
      'run failed long ago',
      'run elsewhere',
      'session live',
      'session approval',
      'session clean',
      'session failed',
      'session failed long ago',
      'session outside',
      'idea captured',
      'idea answering',
      'idea proposed',
      'doing',
      'blocked',
    ]);
  });

  it('Runs and sessions is In flight with tasks off as well', () => {
    const inflight = refs(filterLines(fixed(), PRESETS.inflight, now).drawn);

    expect(refs(filterLines(fixed(), PRESETS.runs, now).drawn)).toEqual(inflight.filter((ref) => ref !== 'doing' && ref !== 'blocked'));
  });
});

describe('stateOf', () => {
  const states = () => Object.fromEntries(fixed().map((line) => [line.ref, stateOf(line, now)]));

  it('calls waiting on you what the header counts: a run asking, a session asking for approval, an idea with questions open', () => {
    const waiting = Object.entries(states()).filter(([, state]) => state === 'waiting').map(([ref]) => ref);

    expect(waiting).toEqual(['run asking', 'session approval', 'idea answering']);
    expect(waiting).toEqual(fixed().filter((line) => line.waiting).map((line) => line.ref));
  });

  it('counts a session that finished its turn as waiting on you too', () => {
    const [line] = linesOf({ runs: [], sessions: [session('turn over', { needs: 'waiting' })], ideas: [], busy: [], tasks: [] }, now);

    expect(stateOf(line, now)).toBe('waiting');
  });

  it('calls a failure failed for a day and ended after that', () => {
    const state = states();

    expect([state['run failed lately'], state['run failed yesterday'], state['session failed']]).toEqual(['failed', 'failed', 'failed']);
    expect(state['session failed long ago']).toBe('ended');
  });

  it('calls ideas being shaped and blocked tasks running, and clean ends ended', () => {
    const state = states();

    expect([state['idea captured'], state['idea proposed'], state.blocked, state.doing]).toEqual(['running', 'running', 'running', 'running']);
    expect([state['run done'], state['session clean']]).toEqual(['ended', 'ended']);
  });
});

describe('each filter', () => {
  const only = (over: Partial<MapFilter>) => refs(filterLines(fixed(), { ...PRESETS.all, ...over }, now).drawn);

  it('turns kinds on one at a time', () => {
    expect(only({ kinds: ['ideas'] })).toEqual(['idea captured', 'idea answering', 'idea proposed']);
    expect(only({ kinds: ['tasks', 'sessions'] })).toEqual(['session live', 'session approval', 'session clean', 'session failed', 'session failed long ago', 'session outside', 'doing', 'blocked']);
  });

  it('picks states', () => {
    expect(only({ states: ['waiting'] })).toEqual(['run asking', 'session approval', 'idea answering']);
    expect(only({ states: ['ended'] })).toEqual(['run done', 'session clean', 'session failed long ago']);
  });

  it('hides the projects named, and (no project) hides work outside any project', () => {
    expect(only({ projects: [NO_PROJECT] })).not.toContain('run elsewhere');
    expect(only({ projects: [NO_PROJECT] })).not.toContain('session outside');
    expect(only({ projects: [NO_PROJECT] })).toContain('session live');
    expect(only({ projects: ['starstats'] })).toEqual(['run elsewhere', 'session outside', 'idea proposed']);
  });

  it('codes what it draws again in drawing order', () => {
    expect(filterLines(fixed(), { ...PRESETS.all, kinds: ['ideas'] }, now).drawn.map((line) => line.code)).toEqual(['1', '2', '3']);
  });
});

describe('the note on what the filters hide', () => {
  it('says how many are hidden and how many of them wait on you', () => {
    const { hidden } = filterLines(fixed(), { ...PRESETS.all, kinds: ['tasks'] }, now);

    expect(hiddenNote(hidden)).toBe('16 hidden, 3 waiting on you');
  });

  it('leaves the waiting part off when none of them waits, and says nothing when nothing is hidden', () => {
    expect(hiddenNote(filterLines(fixed(), { ...PRESETS.all, states: ['waiting', 'running'] }, now).hidden)).toBe('6 hidden');
    expect(hiddenNote(filterLines(fixed(), PRESETS.all, now).hidden)).toBe('');
  });
});

describe('the filters as config holds them', () => {
  it('reads all three unset as In flight', () => {
    expect(filterOf({ mapKinds: null, mapStates: null, mapProjects: null })).toEqual(PRESETS.inflight);
    expect(presetOf(filterOf(null))).toBe('inflight');
  });

  it('reads what is set in the bar order, and the projects as named', () => {
    expect(filterOf({ mapKinds: 'tasks, runs', mapStates: 'ended,waiting', mapProjects: `starstats, ${NO_PROJECT}` })).toEqual({
      kinds: ['runs', 'tasks'],
      states: ['waiting', 'ended'],
      projects: ['starstats', NO_PROJECT],
    });
  });

  it('reads a kinds or states key naming nothing it knows as In flight, never as a blank map', () => {
    expect(filterOf({ mapKinds: 'boats', mapStates: '', mapProjects: null })).toEqual(PRESETS.inflight);
  });

  it('reads the scope of all from a daemon older than the three keys as Everything', () => {
    expect(filterOf({ mapScope: 'all' })).toEqual(PRESETS.all);
    expect(filterOf({ mapScope: 'inflight' })).toEqual(PRESETS.inflight);
  });

  it('names the preset the filters match, in any order, or none for Custom', () => {
    expect(presetOf(PRESETS.all)).toBe('all');
    expect(presetOf({ ...PRESETS.all, states: ['ended', 'failed', 'running', 'waiting'] })).toBe('all');
    expect(presetOf(PRESETS.runs)).toBe('runs');
    expect(presetOf({ ...PRESETS.all, projects: ['starstats'] })).toBeNull();
    expect(presetOf({ ...PRESETS.inflight, kinds: ['runs'] })).toBeNull();
  });

  it('writes a filter as the three keys, Everything with no project hidden', () => {
    expect(keysOf(PRESETS.all)).toEqual([
      ['studio-map-kinds', 'runs, sessions, ideas, tasks'],
      ['studio-map-states', 'waiting, running, failed, ended'],
      ['studio-map-projects', ''],
    ]);
  });
});

describe('the chips', () => {
  it('turns one off, and one back on in its place in the bar', () => {
    expect(toggled(['runs', 'sessions', 'tasks'], 'sessions', ['runs', 'sessions', 'ideas', 'tasks'])).toEqual(['runs', 'tasks']);
    expect(toggled(['runs', 'tasks'], 'ideas', ['runs', 'sessions', 'ideas', 'tasks'])).toEqual(['runs', 'ideas', 'tasks']);
  });

  it('keeps the last one on', () => {
    expect(toggled(['ended'], 'ended', ['waiting', 'running', 'failed', 'ended'])).toEqual(['ended']);
  });

  it('offers every project in view and any hidden with nothing in view, (no project) last', () => {
    expect(projectsIn(fixed(), ['gone'])).toEqual(['gone', 'starstats', 'website', NO_PROJECT]);
  });
});
