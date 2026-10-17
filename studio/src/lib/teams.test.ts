// What each kind of question offers and the exact command each answer runs,
// what the team sheets write and refuse, how Clear out runs says itself, and
// how a delivered file is read: never as HTML, with only http(s) links live.
import { describe, expect, it } from 'vitest';
import { quoted } from './commands';
import { codeTokens, diffLines, fileKind, inline, markdown, parseCsv } from './files';
import { sheetCommand, sheetProblem, sheetValues, type SheetTeam } from './teamSheet';
import { depthKey, gateButtons, gateKind, pruneCommand, pruneSentence, type GateAsk } from './teams';
import { boardRunsOf, boardTeamsOf, nodeWord } from './teamsData';
import type { Run } from './api';

const ask = (over: Partial<GateAsk>): GateAsk => ({ kind: 'question', run: '20261017-1402-ab3f', gate: 'g1', question: 'Which way?', ...over });
const cmds = (asked: GateAsk, typed?: string, figure?: string, reason?: string) => gateButtons(asked, typed, figure, reason).map((one) => one.cmd);

describe('gateButtons', () => {
  it('offers the lead’s options, its recommendation solid, and Think again', () => {
    const buttons = gateButtons(ask({ options: ['Ship it', 'Hold'], recommendation: 'Ship it' }));

    expect(buttons.map((one) => one.label)).toEqual(['Ship it', 'Hold', 'Think again']);
    expect(buttons.map((one) => one.solid)).toEqual([true, false, false]);
    expect(buttons.map((one) => one.cmd)).toEqual([
      '$ loadout team gate 20261017-1402-ab3f --gate g1 --answer "Ship it" --by dashboard',
      '$ loadout team gate 20261017-1402-ab3f --gate g1 --answer Hold --by dashboard',
      '$ loadout team gate 20261017-1402-ab3f --gate g1 --think-again --by dashboard',
    ]);
    expect(buttons[2].send).toEqual({ answer: 'Think again', reason: null, thinkAgain: true });
  });

  it('carries a reason on each answer, quoted as a shell needs it', () => {
    expect(cmds(ask({ options: ['Yes'] }), '', '', 'the tests pass')[0]).toBe(
      '$ loadout team gate 20261017-1402-ab3f --gate g1 --answer Yes --by dashboard --reason "the tests pass"',
    );
  });

  it('accepts readings or sends them back, and nothing else', () => {
    expect(gateButtons(ask({ kind: 'readings' })).map((one) => one.label)).toEqual(['Accept these readings', 'Think again']);
  });

  it('sends a brief as it is, or the person’s instead only when it was changed', () => {
    const brief = ask({ kind: 'brief', brief: 'Fix the README.' });

    expect(cmds(brief, 'Fix the README.')[0]).toBe('$ loadout team gate 20261017-1402-ab3f --gate g1 --answer yes --by dashboard');
    expect(cmds(brief, 'Fix the README and the CHANGELOG.')[0]).toBe(
      '$ loadout team gate 20261017-1402-ab3f --gate g1 --answer yes --instead "Fix the README and the CHANGELOG." --by dashboard',
    );
    expect(gateButtons(brief, 'Something else')[0].label).toBe('Send your brief');
    expect(gateButtons(brief, 'Fix the README.')[0].send.instead).toBeNull();
    expect(cmds(brief)[1]).toBe('$ loadout team gate 20261017-1402-ab3f --gate g1 --answer no --by dashboard');
  });

  it('holds a run to the proposed criteria, or to the ones the person wrote', () => {
    const proposal = ask({ kind: 'proposal', proposal: ['The tests pass', 'The docs say so'] });

    expect(cmds(proposal, 'The tests pass\nThe docs say so')[0]).toBe('$ loadout team gate 20261017-1402-ab3f --gate g1 --answer yes --by dashboard');
    expect(gateButtons(proposal, '').map((one) => one.label)).toEqual(['Hold it to nothing']);
    expect(gateButtons(proposal, 'The tests pass')[0].send.instead).toBe('The tests pass');
  });

  it('answers a confirmation, a permission and a kind it does not know yes or no, with no Think again', () => {
    for (const kind of ['confirm', 'permission'] as const) {
      expect(gateButtons(ask({ kind })).map((one) => one.label)).toEqual(['Yes', 'No']);
    }

    expect(gateKind('remedy')).toBe('confirm');
    expect(gateKind(undefined)).toBe('confirm');
    expect(gateKind('done-when')).toBe('proposal');
    expect(gateButtons(ask({ kind: gateKind('something new') })).some((one) => one.label === 'Think again')).toBe(false);
  });

  it('offers a budget’s answers and a figure of the person’s own', () => {
    const budget = ask({ kind: 'budget', options: ['Raise it to $40', 'Take the cap off'], recommendation: 'Raise it to $40' });

    expect(gateButtons(budget).map((one) => one.label)).toEqual(['Raise it to $40', 'Take the cap off']);
    expect(gateButtons(budget, '', '60').at(-1)).toMatchObject({ label: 'Raise it to $60', cmd: '$ loadout team gate 20261017-1402-ab3f --gate g1 --answer 60 --by dashboard' });
    expect(gateButtons(budget, '', 'none').at(-1)?.label).toBe('No cap');
  });
});

describe('quoted', () => {
  it('quotes and escapes what a shell would read, and leaves a plain word alone', () => {
    expect(quoted('plain')).toBe('plain');
    expect(quoted('')).toBe('""');
    expect(quoted('two words')).toBe('"two words"');
    expect(quoted('cost $5')).toBe('"cost \\$5"');
    expect(quoted('run `this`')).toBe('"run \\`this\\`"');
    expect(quoted('a\\b')).toBe('"a\\\\b"');
    expect(quoted('say "hi"')).toBe('"say \\"hi\\""');
  });
});

const TEAMS: SheetTeam[] = [
  { name: 'iterating-project', description: 'Builds in rounds', nodes: 4, lead: 'lead', posture: 'supervised' },
  { name: 'review-shape', description: 'A shape', nodes: 2, lead: 'lead', posture: 'manual', template: true },
];

describe('the team sheets', () => {
  const base = sheetValues({ team: 'iterating-project', goal: 'Make the docs true', project: 'starstats' });

  it('writes a run with every option it was given', () => {
    const v = { ...base, autonomy: 'autonomous', rounds: '5', usd: '12', done: 'The tests pass\n\nThe docs say so', dropped: ['A CHANGELOG entry'], after: '30m', timeout: 'recommend' as const };

    expect(sheetCommand('run', v)).toBe(
      '$ loadout team run iterating-project "Make the docs true" --project starstats --autonomy autonomous --rounds 5 --usd 12 --done-when "The tests pass" --done-when "The docs say so" --drop-project-done-when "A CHANGELOG entry" --take-recommendation-after 30m --on-timeout recommend',
    );
    expect(sheetProblem('run', v, TEAMS)).toBeNull();
  });

  it('writes a team, and a schedule by interval, time or commit', () => {
    expect(sheetCommand('make', sheetValues({ name: 'quick-review', from: 'review-shape', forProject: true, project: 'starstats' }))).toBe(
      '$ loadout team new quick-review --from review-shape --for-this-project --project starstats',
    );
    expect(sheetCommand('schedule', { ...base, name: 'nightly', usd: '3' })).toBe('$ loadout team schedule add nightly iterating-project "Make the docs true" --project starstats --every 24h --usd 3');
    expect(sheetCommand('schedule', { ...base, name: 'nightly', when: 'at', at: '02:30' })).toContain('--at 02:30');
    expect(sheetCommand('schedule', { ...base, name: 'nightly', when: 'on' })).toContain('--on commit');
  });

  it('refuses a run with no goal, a template, or a budget of nothing', () => {
    expect(sheetProblem('run', { ...base, goal: '  ' }, TEAMS)).toBe('say what the run is for first');
    expect(sheetProblem('run', { ...base, team: 'review-shape' }, TEAMS)).toBe('review-shape is a shape to copy, not a team to run');
    expect(sheetProblem('run', { ...base, usd: '0' }, TEAMS)).toBe('a budget of nothing is refused; say none for no cap');
    expect(sheetProblem('run', { ...base, usd: 'none' }, TEAMS)).toBeNull();
  });

  it('refuses a schedule faster than five minutes, or a manual one', () => {
    const schedule = { ...base, name: 'often' };

    expect(sheetProblem('schedule', { ...schedule, every: '4m' }, TEAMS)).toBe('nothing may repeat faster than every five minutes');
    expect(sheetProblem('schedule', { ...schedule, every: '5m' }, TEAMS)).toBeNull();
    expect(sheetProblem('schedule', { ...schedule, autonomy: 'manual' }, TEAMS)).toMatch(/cannot be manual/);
  });

  it('refuses a team name that is not lowercase and hyphenated, or is taken', () => {
    expect(sheetProblem('make', sheetValues({ name: 'Quick Review' }), TEAMS)).toMatch(/lowercase/);
    expect(sheetProblem('make', sheetValues({ name: 'iterating-project' }), TEAMS)).toBe('a team called iterating-project already exists');
  });
});

describe('Clear out runs', () => {
  it('says what it would do as a sentence, and what it runs', () => {
    expect(pruneSentence({ outcome: 'failed', olderThan: '30d', keep: '20' })).toBe(
      'Moves the failed runs older than 30d to the bin, never the newest 20, and never one still going or one that left a branch nothing merged.',
    );
    expect(pruneSentence({ outcome: '', olderThan: '', keep: '' })).toBeNull();
    expect(pruneCommand({ outcome: 'failed', olderThan: '30d', keep: '' })).toBe('$ loadout team runs prune --failed --older-than 30d --yes');
    expect(pruneCommand({ outcome: 'stopped', olderThan: '', keep: '5' }, true)).toBe('$ loadout team runs prune --outcome stopped --keep 5 --dry-run');
  });
});

describe('depthKey', () => {
  const press = (key: string, target: HTMLElement, init: KeyboardEventInit = {}) => {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, ...init });
    Object.defineProperty(event, 'target', { value: target });

    return depthKey(event);
  };

  it('picks a depth with 1 to 5 on the page, and nothing else', () => {
    expect(press('1', document.body)).toBe(0);
    expect(press('5', document.body)).toBe(4);
    expect(press('6', document.body)).toBeNull();
    expect(press('1', document.body, { ctrlKey: true })).toBeNull();
  });

  it('leaves a key alone in a field, a dialog or a terminal', () => {
    const input = document.createElement('input');
    const dialog = document.createElement('div');
    dialog.setAttribute('role', 'dialog');
    const inDialog = dialog.appendChild(document.createElement('button'));
    const term = document.createElement('div');
    term.className = 'xterm';
    const inTerm = term.appendChild(document.createElement('textarea'));

    expect(press('2', input)).toBeNull();
    expect(press('2', inDialog)).toBeNull();
    expect(press('2', inTerm)).toBeNull();
  });
});

describe('reading delivered files', () => {
  it('knows a file by its name', () => {
    expect(['README.md', 'data.csv', 'out.json', 'logo.svg', 'Program.cs', 'notes', 'build.zip'].map(fileKind)).toEqual(['markdown', 'csv', 'json', 'image', 'code', 'text', 'binary']);
  });

  it('never makes HTML from Markdown, and only follows http and https links', () => {
    const blocks = markdown('# Title\n\n<script>alert(1)</script> and <b>bold</b>\n\n[site](https://example.com) [bad](javascript:alert(1))');

    expect(blocks[0]).toMatchObject({ t: 'h', level: 1 });
    expect(blocks[1]).toEqual({ t: 'p', runs: [{ t: 'text', text: '<script>alert(1)</script> and <b>bold</b>' }] });
    expect(blocks[2]).toMatchObject({ t: 'p' });
    const runs = (blocks[2] as { runs: { t: string; href?: string }[] }).runs;
    expect(runs.filter((run) => run.t === 'link').map((run) => run.href)).toEqual(['https://example.com']);
    // Shown as words beside its target, and never followed.
    expect(runs.find((run) => run.t === 'dead')?.href).toMatch(/^javascript:/);
  });

  it('reads lists, tables, quotes and code blocks', () => {
    const blocks = markdown('- one\n- two\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n> said\n\n```\ncode\n```');

    expect(blocks.map((block) => block.t)).toEqual(['list', 'table', 'quote', 'pre']);
    expect(inline('**strong** and `code`').map((run) => run.t)).toEqual(['strong', 'text', 'code']);
  });

  it('splits a CSV with quoted cells, a line of code into tokens, and a patch into lines', () => {
    expect(parseCsv('a,b\n"x, y",2\n', ',')).toEqual([['a', 'b'], ['x, y', '2']]);
    expect(codeTokens('const x = "hi"; // note', 'ts').map((token) => token.cls)).toEqual(['k', '', 's', '', 'c']);
    expect(diffLines('diff --git a/x b/x\n--- a/x\n+++ b/x\n@@ -1 +1 @@\n-old\n+new\n same')).toEqual([['@', '@@ -1 +1 @@'], ['-', 'old'], ['+', 'new'], [' ', 'same']]);
  });
});

describe('nodeWord', () => {
  it('says a node blocked on your answer is waiting for you, and one blocked otherwise is blocked', () => {
    expect(nodeWord('blocked', 'between turns', true)).toBe('waiting for you');
    expect(nodeWord('blocked', 'between turns', false)).toBe('blocked');
    expect(nodeWord('working', 'working')).toBe('working');
  });
});

describe('Team Focus’s board', () => {
  const run = (id: string, team: string, running: boolean, waitingForYou = false) => ({ id, team, running, waitingForYou }) as unknown as Run;

  it('shows every live run and only the newest few that ended', () => {
    const runs = [run('e1', 'a', false), run('l1', 'a', true), run('e2', 'b', false), run('e3', 'b', false), run('l2', 'b', true)];

    expect(boardRunsOf(runs, 2).map((one) => one.id)).toEqual(['l1', 'l2', 'e1', 'e2']);
  });

  it('marks a team waiting for you where a live run of it is, and running where one goes', () => {
    const runs = [run('l1', 'a', true, true), run('l2', 'b', true), run('e1', 'c', false, true)];

    expect(boardTeamsOf(['a', 'b', 'c'], runs)).toEqual([
      { name: 'a', word: 'waiting for you', fact: '1 live' },
      { name: 'b', word: 'running', fact: '1 live' },
      { name: 'c', word: 'idle', fact: 'none live' },
    ]);
  });
});
