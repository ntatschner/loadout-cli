// QuestionSet, PlanLayer and NoteDump: what each sends is exactly what it
// shows, nothing goes that did not change, and nothing runs after a failure.
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { tick } from 'svelte';
import QuestionSet from './QuestionSet.svelte';
import PlanLayer from './PlanLayer.svelte';
import NoteDump from './NoteDump.svelte';
import { readFileSync } from 'node:fs';
import { acceptRecommendations, changedAnswers, offeredChoices, sendAnswers, type Answer, type Question } from '../ideas';

async function settle() {
  for (let i = 0; i < 5; i++) {
    await tick();
    await Promise.resolve();
  }
}

const said = (button: HTMLElement) => button.closest('.ls-action')?.querySelector('.ls-cmd')?.textContent ?? '';

const QUESTIONS: Question[] = [
  { id: 'Q1', question: 'Beside it, or replace it?', why: 'It decides the screens.', options: ['Beside it', 'Replace it'], recommendation: 'Beside it', answer: null },
  { id: 'Q2', question: 'Who opens it?', why: null, options: ['Only this machine', 'The network'], recommendation: null, answer: null },
  { id: 'Q3', question: 'What first?', why: null, options: [], recommendation: 'The projects', answer: 'The projects' },
];

describe('QuestionSet', () => {
  it('sends only the answers that changed and say something, each joined with =', async () => {
    const sent: { answers: Answer[]; refine: boolean }[] = [];
    render(QuestionSet, { id: 'browser-launcher', where: ['--project', 'loadout-cli'], questions: QUESTIONS, onsend: async (answers: Answer[], refine: boolean) => (sent.push({ answers, refine }), true) });

    // Q1 chosen; Q2 typed then emptied again, so it says nothing.
    await fireEvent.click(screen.getByRole('radio', { name: /Beside it/ }));
    const own = screen.getAllByLabelText('Or in your own words');
    await fireEvent.input(own[1], { target: { value: '   ' } });
    await settle();

    expect(screen.getByText('1 answer ready to send.')).toBeTruthy();
    await fireEvent.click(screen.getByRole('button', { name: 'Only save the answers' }));
    await settle();

    expect(sent).toHaveLength(1);
    expect(sent[0].refine).toBe(false);
    expect(sent[0].answers.map((one) => one.words)).toEqual([['browser-launcher', 'Q1', '--answer=Beside it', '--project', 'loadout-cli']]);
    expect(sent[0].answers[0].line).toBe("$ loadout idea answer browser-launcher Q1 '--answer=Beside it' --project loadout-cli");
  });

  it('does not send an answer changed back to what was saved', async () => {
    render(QuestionSet, { id: 'i', where: ['--global'], questions: QUESTIONS, onsend: async () => true });

    await fireEvent.click(screen.getByRole('button', { name: 'Change it' }));
    await settle();

    // Reopened with the saved answer picked, which is no change. It is the
    // recommendation, so it is a chip, still marked, and not typed text.
    expect((screen.getByRole('radio', { name: /^The projects\s*recommended$/ }) as HTMLInputElement).checked).toBe(true);
    expect((screen.getAllByLabelText('Or in your own words')[2] as HTMLTextAreaElement).value).toBe('');
    const send = screen.getByRole('button', { name: 'Send answers and refine' });
    expect(send.getAttribute('aria-disabled')).toBe('true');
    expect(said(send)).toBe('answer a question first');
  });

  it('runs the refine after the answers, and not after one fails', async () => {
    const ran: string[][] = [];
    const answers: Answer[] = [
      { question: 'Q1', answer: 'a', words: ['i', 'Q1', '--answer=a', '--global'], line: '' },
      { question: 'Q2', answer: 'b', words: ['i', 'Q2', '--answer=b', '--global'], line: '' },
    ];

    expect(await sendAnswers('i', ['--global'], answers, true, async (path, words) => (ran.push([path, ...words]), true))).toBe(true);
    expect(ran.map((one) => one.slice(0, 3))).toEqual([['idea answer', 'i', 'Q1'], ['idea answer', 'i', 'Q2'], ['idea refine', 'i', '--global']]);

    ran.length = 0;
    expect(await sendAnswers('i', ['--global'], answers, true, async (path, words) => (ran.push([path, ...words]), words[1] !== 'Q1'))).toBe(false);
    expect(ran.map((one) => one[0])).toEqual(['idea answer']);
  });

  it('offers a recommendation that is not an option as a chip, with its reason under the matched one', async () => {
    const asked: Question[] = [
      { id: 'Q1', question: 'Beside it, or replace it?', why: null, options: ['Beside it', 'Replace it'], recommendation: 'Beside it, because the screens stay', answer: null },
      { id: 'Q2', question: 'Who opens it?', why: null, options: ['Only this machine', 'The network'], recommendation: 'Neither, for now', answer: null },
    ];
    const sent: Answer[][] = [];
    render(QuestionSet, { id: 'i', where: ['--global'], questions: asked, onsend: async (answers: Answer[]) => (sent.push(answers), true) });

    const first = screen.getByRole('group', { name: 'Beside it, or replace it?' });
    expect(within(first).getByRole('radio', { name: /^Beside it\s*recommended$/ })).toBeTruthy();
    expect(within(first).getByText('Why it recommends Beside it: because the screens stay')).toBeTruthy();
    expect(screen.queryByText(/^Recommended:/)).toBeNull();

    await fireEvent.click(screen.getByRole('radio', { name: /Neither, for now/ }));
    await settle();

    // Picked, not typed: the own-words box stays empty.
    expect((screen.getAllByLabelText('Or in your own words')[1] as HTMLTextAreaElement).value).toBe('');
    await fireEvent.click(screen.getByRole('button', { name: 'Only save the answers' }));
    await settle();
    expect(sent[0].map((one) => one.answer)).toEqual(['Neither, for now']);
  });

  it('takes every recommendation left open with one press, keeping what you drafted, and sends nothing', async () => {
    const sent: Answer[][] = [];
    const asked: Question[] = [
      ...QUESTIONS,
      { id: 'Q4', question: 'Where?', why: null, options: ['Here', 'There'], recommendation: 'There', answer: null },
    ];
    render(QuestionSet, { id: 'i', where: ['--global'], questions: asked, onsend: async (answers: Answer[]) => (sent.push(answers), true) });

    await fireEvent.click(screen.getByRole('radio', { name: /^Here/ }));
    await settle();

    const take = screen.getByRole('button', { name: 'Accept all recommendations' });
    expect(said(take)).toBe('fills 1 answer; sends nothing');
    await fireEvent.click(take);
    await settle();

    expect(sent).toHaveLength(0);
    expect((screen.getByRole('radio', { name: /^Beside it/ }) as HTMLInputElement).checked).toBe(true);
    expect((screen.getByRole('radio', { name: /^Here/ }) as HTMLInputElement).checked).toBe(true);
    expect(screen.getByText('2 answers ready to send.')).toBeTruthy();

    const again = screen.getByRole('button', { name: 'Accept all recommendations' });
    expect(again.getAttribute('aria-disabled')).toBe('true');
    expect(said(again)).toBe('no recommendations left to take');
  });
});

interface RecommendationCase {
  name: string;
  options: string[];
  recommendation: string | null;
  choices: string[];
  recommended: number | null;
  note: string | null;
}

// The same table IdeaWorkTests reads, so the page and the ingest fix-up agree.
const CASES = JSON.parse(readFileSync('../tests/Loadout.Tests/Fixtures/recommendations.json', 'utf-8')) as RecommendationCase[];

describe('offeredChoices', () => {
  it.each(CASES.map((one) => [one.name, one] as const))('%s', (_, one) => {
    expect(offeredChoices({ options: one.options, recommendation: one.recommendation })).toEqual({ choices: one.choices, recommended: one.recommended, note: one.note });
  });
});

describe('acceptRecommendations', () => {
  const asked: Question[] = [
    { id: 'Q1', question: 'a', why: null, options: ['Yes', 'No'], recommendation: 'Yes', answer: null },
    { id: 'Q2', question: 'b', why: null, options: ['Yes', 'No'], recommendation: 'No', answer: 'Yes' },
    { id: 'Q3', question: 'c', why: null, options: ['Yes', 'No'], recommendation: 'Yes', answer: null },
    { id: 'Q4', question: 'd', why: null, options: ['Yes', 'No'], recommendation: '', answer: null },
    { id: 'Q5', question: 'e', why: null, options: ['Yes'], recommendation: 'Maybe later', answer: null },
  ];

  it('fills only open questions with nothing drafted, and skips an empty recommendation', () => {
    const drafts = acceptRecommendations(asked, { Q3: 'No' }, (one) => !one.answer);

    expect(drafts).toEqual({ Q1: 'Yes', Q3: 'No', Q5: 'Maybe later' });
    expect(changedAnswers('i', ['--global'], asked, drafts).map((one) => one.line)).toEqual([
      '$ loadout idea answer i Q1 --answer=Yes --global',
      '$ loadout idea answer i Q3 --answer=No --global',
      "$ loadout idea answer i Q5 '--answer=Maybe later' --global",
    ]);
  });
});

const LAYER = {
  id: 'L2',
  name: 'How it is served',
  purpose: 'Where the page comes from.',
  chosen: 'o1',
  verdict: 'Undecided',
  options: [
    { id: 'o1', title: 'From the daemon', pros: ['One process'], cons: ['Must be running'], recommended: true },
    { id: 'o2', title: 'Its own server' },
  ],
};

describe('PlanLayer', () => {
  it('offers Choose this on every option but the chosen one', () => {
    render(PlanLayer, { idea: 'i', where: ['--global'], layer: LAYER });

    const cards = screen.getAllByRole('article');
    expect(within(cards[0]).queryByRole('button', { name: 'Choose this' })).toBeNull();
    const choose = within(cards[1]).getByRole('button', { name: 'Choose this' });
    expect(said(choose)).toBe('$ loadout idea choose i L2 o2 --global');
  });

  it('reads the verdict as idea show writes it, in lower case', () => {
    render(PlanLayer, { idea: 'i', where: ['--global'], layer: { ...LAYER, verdict: 'keep' } });

    const keep = screen.getByRole('button', { name: 'Keep it' });
    expect(keep.getAttribute('aria-disabled')).toBe('true');
    expect(said(keep)).toBe('kept already');
    expect(screen.getByText('verdict: keep').classList.contains('good')).toBe(true);
  });

  it('offers nothing once the idea is accepted', () => {
    render(PlanLayer, { idea: 'i', where: ['--global'], layer: LAYER, accepted: true });

    expect(screen.queryByRole('button', { name: 'Choose this' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Keep it' })).toBeNull();
  });

  it('keeps Send it back disabled until there is a request', async () => {
    const asked: string[] = [];
    render(PlanLayer, { idea: 'i', where: ['--project', 'p'], layer: LAYER, onimprove: (request: string) => asked.push(request) });

    await fireEvent.click(screen.getByRole('button', { name: 'Ask for it to be reworked' }));
    const send = screen.getByRole('button', { name: 'Send it back' });
    expect(send.getAttribute('aria-disabled')).toBe('true');
    expect(said(send)).toBe('say what should change');

    await fireEvent.input(screen.getByLabelText('What should change about L2'), { target: { value: 'Serve it from both' } });
    await settle();

    const ready = screen.getByRole('button', { name: 'Send it back' });
    expect(said(ready)).toBe("$ loadout idea improve i L2 '--request=Serve it from both' --project p");
    await fireEvent.click(ready);
    expect(asked).toEqual(['Serve it from both']);
  });
});

describe('NoteDump', () => {
  const dump = {
    id: 'd3',
    source: 'Notes app',
    captured: '2026-10-16T21:40:00Z',
    split: '2026-10-16T21:41:00Z',
    items: [
      { number: 1, title: 'A dark mode', kind: 'idea' },
      { number: 2, title: 'Fix the flaky test', kind: 'task' },
      { number: 3, title: 'Ask about the bucket', kind: 'task', recorded: 'starstats/ask-about-the-bucket' },
    ],
  };

  it('records every unrecorded piece without --only, and names them once one is unticked', async () => {
    const applied: string[][] = [];
    render(NoteDump, { dump, onapply: (words: string[]) => applied.push(words) });

    const record = screen.getByRole('button', { name: /^Record/ });
    expect(said(record)).toBe('$ loadout idea dump apply d3');

    await fireEvent.click(screen.getAllByRole('checkbox')[1]);
    await settle();

    const now = screen.getByRole('button', { name: /^Record/ });
    expect(said(now)).toBe('$ loadout idea dump apply d3 --only 1');
    await fireEvent.click(now);
    expect(applied).toEqual([['d3', '--only', '1']]);
  });
});
