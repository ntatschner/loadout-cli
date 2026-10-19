// Next on the Ideas workbench: open questions first, the send button once
// every one has something typed, a half-written rework, the plan's gaps in
// page order, and Accept when nothing is left.
import { describe, expect, it } from 'vitest';
import { gaps, needsChoice, nextStop, NO_DRAFTS, stopTarget, stopWords, type Piece, type Question } from './ideas';

const question = (id: string, answer: string | null = null): Question => ({ id, question: `${id}?`, why: null, options: [], recommendation: null, answer });
const layer = (id: string, chosen: string | null, verdict = 'undecided'): Piece => ({ id, verdict, chosen, options: [{ id: 'A' }, { id: 'B' }] });
const addition = (id: string, verdict = 'undecided'): Piece => ({ id, verdict });

function idea(questions: Question[], layers: Piece[] = [], additions: Piece[] = [], accepted: unknown = null) {
  return { questions, plan: layers.length || additions.length ? { layers, additions } : null, accepted };
}

describe('nextStop', () => {
  it('goes to the first open question, counted among all of them', () => {
    const stop = nextStop(idea([question('Q1', 'yes'), question('Q2'), question('Q3')]));

    expect(stop).toEqual({ kind: 'question', id: 'Q2', index: 2, of: 3 });
    expect(stopWords(stop!)).toBe('answer 2 of 3');
  });

  it('passes over questions with an answer typed or sent, while others are still open', () => {
    const stop = nextStop(idea([question('Q1'), question('Q2'), question('Q3')]), { answers: ['Q1'], sent: ['Q2'], reworks: [] });

    expect(stop).toMatchObject({ kind: 'question', id: 'Q3' });
  });

  it('goes to Send answers once every question has something typed', () => {
    const stop = nextStop(idea([question('Q1'), question('Q2', 'saved')], [layer('L1', null)]), { answers: ['Q1'], sent: [], reworks: [] });

    expect(stop).toEqual({ kind: 'send-answers' });
  });

  it('goes to Send it back for a half-written rework, before the plan gaps', () => {
    const stop = nextStop(idea([], [layer('L1', null), layer('L2', 'A')]), { ...NO_DRAFTS, reworks: ['L2'] });

    expect(stop).toEqual({ kind: 'rework', id: 'L2' });
    expect(stopTarget(stop!)).toBe('rework:L2');
  });

  it('takes a layer choice before its verdict, and the layers before the additions', () => {
    const plan = idea([], [layer('L1', 'A', 'keep'), layer('L2', null)], [addition('A1')]);

    expect(gaps(plan).map(stopTarget)).toEqual(['choose:L2', 'verdict:L2', 'verdict:A1']);
    expect(nextStop(plan)).toEqual({ kind: 'choose', id: 'L2' });
  });

  it('skips dropped layers, and a layer sent back to be reworked', () => {
    const plan = idea([], [layer('L1', null, 'drop'), layer('L2', null, 'improve'), layer('L3', 'B', 'keep')], [addition('A1', 'drop')]);

    expect(gaps(plan).map(stopTarget)).toEqual(['choose:L2']);
  });

  it('goes to Accept when nothing is missing and nothing is unsent', () => {
    expect(nextStop(idea([question('Q1', 'yes')], [layer('L1', 'A', 'keep')], [addition('A1', 'keep')]))).toEqual({ kind: 'accept' });
  });

  it('goes nowhere on an accepted idea, or one with no plan yet and nothing open', () => {
    expect(nextStop(idea([question('Q1')], [layer('L1', null)], [], { project: 'p' }))).toBeNull();
    expect(nextStop(idea([question('Q1', 'yes')]))).toBeNull();
  });
});

describe('needsChoice', () => {
  it('is what the Accept warning counts: options, none chosen, not dropped', () => {
    expect([layer('L1', null), layer('L2', 'A'), layer('L3', null, 'drop'), addition('A1')].map(needsChoice)).toEqual([true, false, false, false]);
  });
});
