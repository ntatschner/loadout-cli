// The workbench holding still: a choice that fails says it was put back; a
// round that brings a new revision takes you to its heading, unless you have
// scrolled since it started, when the page offers to instead.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';
import type { Job } from '../lib/api';

let revision = 1;
let refusal = '';
const job = (id: string, state: Job['state'], error = ''): Job => ({ id, path: '', line: '', state, exitCode: state === 'done' ? 0 : 1, startedAt: '', endedAt: null, truncated: false, output: '', error });

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();

  return {
    ...real,
    readJson: vi.fn(async (path: string) => {
      if (path === 'idea list') {
        return { ideas: [{ id: 'i1', title: 'An idea', project: 'p', stage: 'proposed', state: 'open', unanswered: 0, running: false }] };
      }

      if (path === 'idea show') {
        return {
          id: 'i1',
          project: 'p',
          stage: 'proposed',
          ask: 'Ask',
          questions: [],
          plan: {
            revision,
            title: 'A plan',
            understanding: '',
            layers: [{ id: 'L1', name: 'Layer L1', purpose: '', chosen: null, verdict: 'undecided', options: [{ id: 'A', title: 'Option A', detail: '', pros: [], cons: [], recommended: false }] }],
            additions: [],
            placement: null,
          },
          accepted: null,
        };
      }

      return new Promise(() => undefined);
    }),
    memory: { ...real.memory, projects: vi.fn(async () => []) },
    commands: {
      ...real.commands,
      run: vi.fn(async (path: string) => (refusal ? job(path, 'failed', refusal) : job(path, 'done'))),
      jobs: vi.fn(async () => []),
    },
  };
});

const { default: Ideas } = await import('./Ideas.svelte');
const { studio } = await import('../lib/state.svelte');
const { toasts } = await import('../lib/toasts.svelte');

async function settle(rounds = 10) {
  for (let i = 0; i < rounds; i++) {
    await tick();
    await Promise.resolve();
  }
}

// A job ending is what makes the page read the idea again.
let ended = 0;

async function aJobEnds() {
  studio.jobs = [...studio.jobs, job(`j${++ended}`, 'done')];
  await settle();
}

describe('The Ideas workbench, holding still', () => {
  it('says a choice was put back when it was not recorded', async () => {
    refusal = 'L1 has no option A';
    render(Ideas, { rest: ['p', 'i1'] });

    await fireEvent.click(await vi.waitFor(() => screen.getByRole('button', { name: 'Choose this' })));
    await settle();

    expect(toasts.list.map((one) => one.text)).toContain('Choosing A for L1 was not recorded, so nothing is chosen. L1 has no option A');
    expect(screen.getByRole('article', { name: /^Option A/ }).getAttribute('aria-label')).toBe('Option A');
    refusal = '';
  });

  it('goes to the top of a new revision and puts focus on its heading', async () => {
    revision = 1;
    const went = vi.fn();
    Element.prototype.scrollIntoView = went;
    const { container } = render(Ideas, { rest: ['p', 'i1'] });

    await vi.waitFor(() => expect(container.querySelector('#ip')?.textContent).toBe('The plan · revision 1'));
    revision = 2;
    await aJobEnds();

    await vi.waitFor(() => expect(container.querySelector('#ip')?.textContent).toBe('The plan · revision 2'));
    expect(document.activeElement).toBe(container.querySelector('#ip'));
    expect(went.mock.contexts).toContain(container.querySelector('#ip'));
  });

  it('stays put and offers the new revision when you scrolled while the round ran', async () => {
    revision = 1;
    const went = vi.fn();
    Element.prototype.scrollIntoView = went;
    const { container } = render(Ideas, { rest: ['p', 'i1'] });

    await vi.waitFor(() => expect(container.querySelector('#ip')?.textContent).toBe('The plan · revision 1'));
    await new Promise((resolve) => setTimeout(resolve, 5));
    await fireEvent.wheel(window);
    revision = 2;
    await aJobEnds();

    const offer = await vi.waitFor(() => screen.getByRole('button', { name: 'New revision 2: go to it' }));
    expect(went).not.toHaveBeenCalled();

    await fireEvent.click(offer);
    expect(document.activeElement).toBe(container.querySelector('#ip'));
    expect(screen.queryByRole('button', { name: 'New revision 2: go to it' })).toBeNull();
  });

  it('does not move for a reload that brings nothing new', async () => {
    revision = 1;
    const went = vi.fn();
    Element.prototype.scrollIntoView = went;
    const { container } = render(Ideas, { rest: ['p', 'i1'] });

    await vi.waitFor(() => expect(container.querySelector('#ip')).toBeTruthy());
    await aJobEnds();

    expect(went).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: /^New revision/ })).toBeNull();
  });
});
