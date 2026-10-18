// QuestionSet, PlanLayer and NoteDump: what each sends is exactly what it
// shows, nothing goes that did not change, and nothing runs after a failure.
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { tick } from 'svelte';
import QuestionSet from './QuestionSet.svelte';
import PlanLayer from './PlanLayer.svelte';
import NoteDump from './NoteDump.svelte';
import { sendAnswers, type Answer, type Question } from '../ideas';

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

    // Reopened with the saved answer in the box, which is no change.
    expect((screen.getAllByLabelText('Or in your own words')[2] as HTMLTextAreaElement).value).toBe('The projects');
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
