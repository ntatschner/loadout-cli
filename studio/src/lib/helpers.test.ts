// The commands Studio shows, its layouts and its lines. The command lines
// are pinned to the same literals as StudioCommandTests on the C# side, which
// checks them against DashboardActions.Maps and DashboardTasks.Maps: a change
// to either side alone fails one of the two.
import { describe, expect, it } from 'vitest';
import { command } from './commands';
import { LENS_PANELS, hide, move, place, readLayout, saved, show } from './layout';
import { codeOf, fromRun, fromTask, linesOf } from './lines';
import type { Run } from './api';

describe('commands', () => {
  it('match what the daemon runs for the same verbs', () => {
    expect(command.gate('20261016-0840-3f1a', 'gate-5e1d9a20', 'Approve')).toBe('$ loadout team gate 20261016-0840-3f1a --gate gate-5e1d9a20 --answer Approve --by dashboard');
    expect(command.gate('r1', null, 'no')).toBe('$ loadout team gate r1 --answer no --by dashboard');
    expect(command.pause('20261016-0840-3f1a')).toBe('$ loadout team halt 20261016-0840-3f1a --pause');
    expect(command.resume('20261016-0840-3f1a')).toBe('$ loadout team halt 20261016-0840-3f1a --resume');
    expect(command.stop('20261016-0840-3f1a')).toBe('$ loadout team halt 20261016-0840-3f1a');
    expect(command.task('upload-retries', 'doing', 'starstats')).toBe('$ loadout task declare upload-retries doing --project starstats');
    expect(command.task('chores', 'done', null)).toBe('$ loadout task declare chores done --global');
  });

  it('quotes an answer with a space in it', () => {
    expect(command.gate('r1', null, 'Not yet')).toBe('$ loadout team gate r1 --answer "Not yet" --by dashboard');
  });

  it('writes an unset setting as an empty value', () => {
    expect(command.setting('studio-layout-lens', '')).toBe('$ loadout config set studio-layout-lens ""');
    expect(command.setting('studio-halo', 'gyroscope')).toBe('$ loadout config set studio-halo gyroscope');
  });
});

describe('layouts', () => {
  it('mends what it reads: unknown panels dropped, missing ones added', () => {
    expect(readLayout('{"order":["all","nonsense","needs"],"hidden":["signal","bogus"]}', LENS_PANELS)).toEqual({
      order: ['all', 'needs', 'focus', 'start', 'signal'],
      hidden: ['signal'],
    });
    expect(readLayout('not json', LENS_PANELS).order).toEqual([...LENS_PANELS]);
  });

  it('moves, places, hides and shows', () => {
    const plain = readLayout(null, LENS_PANELS);

    expect(move(plain, 'needs', 1).order.slice(0, 2)).toEqual(['focus', 'needs']);
    expect(move(plain, 'needs', -1)).toBe(plain);
    expect(place(plain, 'signal', 'needs').order[0]).toBe('signal');
    expect(show(hide(plain, 'start'), 'start').hidden).toEqual([]);
  });

  it('keeps nothing for the default layout, so a reset unsets the key', () => {
    const plain = readLayout(null, LENS_PANELS);

    expect(saved(plain, LENS_PANELS)).toBe('');
    expect(JSON.parse(saved(hide(plain, 'signal'), LENS_PANELS))).toEqual({ order: [...LENS_PANELS], hidden: ['signal'] });
  });
});

describe('lines', () => {
  const run: Run = {
    id: 'r1', team: 'docs-crew', goal: 'Bring the README up to date', running: true, waitingForYou: true,
    ended: null, outcome: 'running', project: 'storefront', started: '2026-10-16T08:40:00Z', finished: null,
    rounds: 2, roundLimit: 0, elapsedSeconds: 3120,
    coverage: [{ criterion: 'Every command runs', verdict: 'met', verdictInWords: 'met', met: true }],
    gates: [{ id: 'g1', kind: 'question', node: 'writer', role: 'role.docs-writer', question: 'Open a pull request?', options: ['Approve', 'Refuse'], recommendation: 'Approve', at: '2026-10-16T09:30:00Z', until: null }],
    budget: { spent: 2.5, cap: 10, uncapped: false },
    nodes: [{ node: 'lead', role: 'role.project-lead', state: 'working', activity: 'coordinating', doing: null, lastSeen: null }],
    attention: [],
  };

  it('turns a run asking a question into a waiting line at the interchange', () => {
    const line = fromRun(run, Date.parse('2026-10-16T09:32:00Z'));

    expect(line.waiting).toBe(true);
    expect(line.stage).toBe(3);
    expect(line.ask).toBe('Open a pull request?');
    expect(line.checks).toEqual([['Every command runs', 'met']]);
    expect(line.mins).toBe(52);
  });

  it('calls a failed run a problem', () => {
    const line = fromRun({ ...run, running: false, waitingForYou: false, gates: [], outcome: 'failed', ended: 'the lead failed' }, Date.now());

    expect(line.problem).toBe(true);
    expect(line.failed).toContain('the lead failed');
  });

  it('draws a blocked task with what it is waiting on', () => {
    const line = fromTask({ id: 'new-bucket', title: 'Move uploads', state: 'blocked', note: 'waiting on access', declared: '' }, 'starstats');

    expect(line.blocked).toBe('waiting on access');
    expect(line.stage).toBe(2);
  });

  it('keeps a line its code from one read to the next', () => {
    expect(codeOf('run:r1')).toBe(codeOf('run:r1'));
    expect(Number(codeOf('session:abc'))).toBeGreaterThanOrEqual(1);
  });

  it('leaves out open tasks and accepted ideas, which are not in flight', () => {
    const lines = linesOf({
      runs: [], sessions: [], busy: [],
      ideas: [{ title: 'Done idea', detail: { id: 'i', project: null, stage: 'accepted', captured: '', error: null, lastRound: null, questions: [] } }],
      tasks: [{ project: 'p', tasks: [{ id: 'o', title: 'Open one', state: 'open', note: null, declared: '' }] }],
    });

    expect(lines).toEqual([]);
  });
});
