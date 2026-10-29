import { describe, expect, it } from 'vitest';
import { backfillLine, backfillPatience, backfillSummary, markWord, type BackfillOutcome } from './backfill';

const outcome = (found: BackfillOutcome['found'], calls: number): BackfillOutcome => ({ lists: ['-'], dryRun: true, calls, found, results: [] });

describe('backfill', () => {
  it('names each mark, and nothing for a record without one', () => {
    expect(markWord('missing')).toBe('missing');
    expect(markWord('proposalWaiting')).toBe('proposal waiting');
    expect(markWord(undefined)).toBeNull();
    expect(markWord(null)).toBeNull();
  });

  it('counts the records and the calls a pass would make, and says when some are left alone', () => {
    const found: BackfillOutcome['found'] = [
      { kind: 'idea', project: 'website', id: 'status', title: 'Status', missing: ['doneWhen'], asks: true },
      { kind: 'task', project: 'website', id: 'login', title: 'Login', missing: ['doneWhen'], asks: false, skipped: 'A proposed done-when is waiting.' },
    ];

    expect(backfillSummary(outcome(found, 1))).toBe('2 records short of the current contract. A pass would ask an agent 1 time, and leave 1 alone, each with its reason.');
    expect(backfillSummary(outcome([], 0))).toBe('Nothing on any list is short of the current contract.');
  });

  it('gives a skipped record its reason and a failed one what went wrong last time', () => {
    expect(backfillLine({ kind: 'task', project: null, id: 'login', title: 'Login', missing: ['doneWhen'], asks: false, skipped: 'A proposed done-when is waiting.' }))
      .toBe('task workspace-wide/login, lacks a done-when. Skipped: A proposed done-when is waiting.');
    expect(backfillLine({ kind: 'idea', project: 'website', id: 'status', title: 'Status', missing: ['doneWhen'], asks: true, lastFailure: 'The agent timed out.' }))
      .toBe('idea website/status, lacks a done-when. Last attempt failed: The agent timed out.');
    expect(backfillLine({ kind: 'task', project: 'website', id: 'later', title: 'Later', missing: ['doneWhen'], asks: false }))
      .toBe('task website/later, lacks a done-when. Left for a later pass.');
  });

  it('waits longer for a pass that asks more', () => {
    expect(backfillPatience(0)).toBe(120_000);
    expect(backfillPatience(3)).toBe(720_000);
  });
});
