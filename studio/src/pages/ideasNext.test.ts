// Next on the Ideas workbench, through the page: the drafts the questions
// report reach it, so it goes to the open question and then, once something
// is typed in every one, to Send answers and refine.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();

  return {
    ...real,
    readJson: vi.fn(async (path: string, words: string[] = []) => {
      if (path === 'idea list') {
        return { ideas: [{ id: 'i1', title: 'An idea', project: 'p', stage: 'answering', state: 'open', unanswered: 2, running: false }] };
      }

      if (path === 'idea show' && words[0] === 'i2') {
        return { id: 'i2', project: 'p', stage: 'proposed', ask: 'Ask', questions: [], plan: PLAN, accepted: null };
      }

      if (path === 'idea show') {
        return {
          id: 'i1',
          project: 'p',
          stage: 'answering',
          ask: 'Ask',
          questions: [
            { id: 'Q1', question: 'Which?', why: null, options: ['This', 'That'], recommendation: null, answer: null },
            { id: 'Q2', question: 'Where?', why: null, options: ['Here', 'There'], recommendation: null, answer: null },
          ],
          plan: null,
          accepted: null,
        };
      }

      return new Promise(() => undefined);
    }),
    memory: { ...real.memory, projects: vi.fn(async () => []) },
    commands: { ...real.commands, jobs: vi.fn(async () => []) },
  };
});

// One idea with questions, one with a plan of two layers and nothing answered.
const PLAN = {
  revision: 1,
  title: 'A plan',
  understanding: '',
  layers: ['L1', 'L2'].map((id) => ({ id, name: `Layer ${id}`, purpose: '', chosen: null, verdict: 'undecided', options: [{ id: 'A', title: 'Option A', detail: '', pros: [], cons: [], recommended: false }] })),
  additions: [],
  placement: null,
};

const { default: Ideas } = await import('./Ideas.svelte');

describe('Next', () => {
  it('goes to the open question, then to Send answers once each has an answer typed', async () => {
    render(Ideas, { rest: ['p', 'i1'] });

    const next = await vi.waitFor(() => screen.getByRole('button', { name: 'Next: answer 1 of 2' }));
    await fireEvent.click(next);
    expect(document.activeElement).toBe(screen.getByRole('radio', { name: 'This' }));

    await fireEvent.click(screen.getByRole('radio', { name: 'This' }));
    await fireEvent.click(await vi.waitFor(() => screen.getByRole('button', { name: 'Next: answer 2 of 2' })));
    expect(document.activeElement).toBe(screen.getByRole('radio', { name: 'Here' }));

    await fireEvent.click(screen.getByRole('radio', { name: 'There' }));
    await fireEvent.click(await vi.waitFor(() => screen.getByRole('button', { name: 'Next: send answers' })));
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Send answers and refine' }));
  });

  it('answers to ] outside a text field', async () => {
    render(Ideas, { rest: ['p', 'i1'] });

    await vi.waitFor(() => screen.getByRole('button', { name: 'Next: answer 1 of 2' }));
    await fireEvent.keyDown(document.body, { key: ']' });
    expect(document.activeElement).toBe(screen.getByRole('radio', { name: 'This' }));
  });

  it('goes to Send it back while a rework is written, and back to the plan once it is put away', async () => {
    render(Ideas, { rest: ['p', 'i2'] });

    await vi.waitFor(() => screen.getByRole('button', { name: 'Next: choose for L1' }));
    const asks = screen.getAllByRole('button', { name: 'Ask for it to be reworked' });
    await fireEvent.click(asks[1]);
    await fireEvent.input(screen.getByLabelText('What should change about L2'), { target: { value: 'Smaller' } });

    await fireEvent.click(await vi.waitFor(() => screen.getByRole('button', { name: 'Next: send L2 back' })));
    expect(document.activeElement).toBe(screen.getAllByRole('button', { name: 'Send it back' })[1]);

    await fireEvent.click(asks[1]);
    await vi.waitFor(() => screen.getByRole('button', { name: 'Next: choose for L1' }));
  });
});
