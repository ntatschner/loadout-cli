// The terminal's states, as the Terminal README lists them: which one a
// session is in, and what it lets you do. A question comes only from the
// agent's own hooks (needs), never from what is on the screen.
import { describe, expect, it } from 'vitest';
import type { Session } from './api';
import { signalOf } from './signals';
import { TERMINAL_STATES, largestFitting, resumeCommand, terminalState, type TerminalFacts } from './terminal';

const session: Session = {
  id: 'a1b2c3d4',
  title: 'starstats',
  startedAt: '2026-10-17T10:00:00Z',
  ended: false,
  exitCode: null,
  viewers: 1,
  columns: 80,
  rows: 24,
  terminal: '/api/v1/sessions/a1b2c3d4/terminal',
  memory: null,
  project: 'starstats',
  agent: 'claude',
  mode: null,
  task: null,
  taskId: null,
  command: null,
  lastOutputAt: '2026-10-17T10:01:00Z',
};

const facts = (over: Partial<TerminalFacts> = {}): TerminalFacts => ({
  session,
  connected: true,
  dropped: false,
  exited: undefined,
  lost: false,
  watchOnly: false,
  quiet: 0,
  idleAfter: 10,
  ...over,
});

describe('terminalState', () => {
  it('is working while it writes, and idle once quiet for studio-idle-after', () => {
    expect(terminalState(facts())).toBe('working');
    expect(terminalState(facts({ quiet: 10 }))).toBe('idle');
  });

  it('is unknown before the session has written anything', () => {
    expect(terminalState(facts({ quiet: Infinity }))).toBe('unknown');
  });

  it("says waiting or needs approval only when the agent's hooks did", () => {
    expect(terminalState(facts({ session: { ...session, needs: 'waiting' } }))).toBe('waiting');
    expect(terminalState(facts({ session: { ...session, needs: 'approval' } }))).toBe('approval');
    expect(terminalState(facts({ session: { ...session, needs: null } }))).toBe('working');
  });

  it('is quiet while the agent waits on its own background work, and a question still comes first', () => {
    expect(terminalState(facts({ quiet: 30, session: { ...session, background: true } }))).toBe('background');
    expect(terminalState(facts({ session: { ...session, background: true, needs: 'approval' } }))).toBe('approval');
    expect(TERMINAL_STATES.background.ask).toBeUndefined();
    expect(signalOf(TERMINAL_STATES.background.word).family).toBe('rest');
  });

  it('tells a clean end from an error by the exit code', () => {
    expect(terminalState(facts({ exited: 0 }))).toBe('ended');
    expect(terminalState(facts({ exited: null }))).toBe('ended');
    expect(terminalState(facts({ exited: 2 }))).toBe('error');
    expect(terminalState(facts({ session: { ...session, ended: true, exitCode: 1 }, connected: false }))).toBe('error');
  });

  it('puts an ending before a dropped connection, and a drop before watching', () => {
    expect(terminalState(facts({ exited: 0, dropped: true }))).toBe('ended');
    expect(terminalState(facts({ dropped: true, connected: false, watchOnly: true }))).toBe('reconnecting');
    expect(terminalState(facts({ watchOnly: true, session: { ...session, needs: 'approval' } }))).toBe('watch');
  });

  it('is connecting until the socket opens, and lost when an earlier daemon had it', () => {
    expect(terminalState(facts({ connected: false }))).toBe('connecting');
    expect(terminalState(facts({ session: undefined }))).toBe('connecting');
    expect(terminalState(facts({ session: undefined, lost: true }))).toBe('lost');
  });
});

describe('TERMINAL_STATES', () => {
  it('lets you type only in a live session', () => {
    const typing = Object.entries(TERMINAL_STATES).filter(([, one]) => one.typing).map(([name]) => name);

    expect(typing.sort()).toEqual(['approval', 'background', 'idle', 'unknown', 'waiting', 'working']);
  });

  it('offers resume wherever the session has stopped, and puts no choice on a button', () => {
    expect(['ended', 'error', 'lost'].every((name) => TERMINAL_STATES[name as keyof typeof TERMINAL_STATES].ask?.resume)).toBe(true);
    expect(TERMINAL_STATES.approval.ask?.button).toBe('Go to the question');
    expect(TERMINAL_STATES.approval.ask?.text).toContain('answer them there');
  });
});

describe('resumeCommand', () => {
  it('names the conversation where the agent gave its id, and the project it belongs to', () => {
    expect(resumeCommand('5f0c1e2a-9d3b', 'starstats')).toBe('$ loadout resume 5f0c1e2a-9d3b --project starstats');
    expect(resumeCommand(null, 'starstats')).toBe('$ loadout resume --last --project starstats');
    expect(resumeCommand('5f0c1e2a-9d3b', null)).toBe('$ loadout resume 5f0c1e2a-9d3b --all');
  });
});

describe('largestFitting', () => {
  // A cell as xterm makes one at a device pixel ratio of 1: whole pixels,
  // about 0.6em, so several sizes share a width.
  const cell = (size: number) => Math.floor(size * 0.6);
  const fitsIn = (width: number) => (size: number) => Math.floor(width / cell(size)) >= 80;

  it('picks the largest size whose 80 columns still go in, to a tenth of a pixel', () => {
    expect(largestFitting(fitsIn(605))).toBe(13.3);
    expect(cell(13.3) * 80).toBeLessThanOrEqual(605);
    expect(cell(13.4) * 80).toBeGreaterThan(605);
  });

  it('stays within the text sizes the bar allows', () => {
    expect(largestFitting(fitsIn(4000))).toBe(22);
    expect(largestFitting(fitsIn(100))).toBe(5);
  });

  it('asks only a handful of times', () => {
    let asked = 0;
    largestFitting((size) => (asked++, fitsIn(900)(size)));

    expect(asked).toBeLessThanOrEqual(8);
  });
});
