// Add to it, and take a note back: the box to refine now starts ticked, an
// unticked box sends no refine at all, and only a note no round has read
// offers its removal.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';

const sent: Record<string, unknown>[] = [];

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();

  return {
    ...real,
    readJson: vi.fn(async (path: string) => {
      if (path === 'idea list') {
        return { ideas: [{ id: 'i1', title: 'An idea', project: 'p', stage: 'ready', state: 'open', unanswered: 0, running: false }] };
      }

      if (path === 'idea show') {
        return {
          id: 'i1', project: 'p', stage: 'ready', ask: 'Ask', questions: [], plan: null, accepted: null,
          notes: [
            { id: 'N1', text: 'Read already', by: 'me', added: '2026-10-22T00:00:00Z', taken: '2026-10-22T01:00:00Z' },
            { id: 'N2', text: 'Not read yet', by: 'me', added: '2026-10-22T02:00:00Z', taken: null },
          ],
        };
      }

      return new Promise(() => undefined);
    }),
    memory: { ...real.memory, projects: vi.fn(async () => []) },
    ideas: {
      ...real.ideas,
      act: vi.fn(async (body: Record<string, unknown>) => {
        sent.push(body);

        return '';
      }),
    },
    commands: { ...real.commands, jobs: vi.fn(async () => []) },
  };
});

const { default: Ideas } = await import('./Ideas.svelte');

describe('Add to it', () => {
  it('starts with Refine now ticked', async () => {
    render(Ideas, { rest: ['p', 'i1'] });

    const box = await vi.waitFor(() => screen.getByRole('checkbox', { name: 'Refine now' }) as HTMLInputElement);

    expect(box.checked).toBe(true);
  });

  it('sends refine while the box is ticked and leaves it out once unticked', async () => {
    sent.length = 0;
    render(Ideas, { rest: ['p', 'i1'] });

    const boxes = await vi.waitFor(() => screen.getAllByRole('checkbox', { name: 'Refine now' }) as HTMLInputElement[]);
    const box = boxes[boxes.length - 1];
    const typed = screen.getAllByLabelText('What to add to it');
    const add = () => screen.getAllByRole('button', { name: 'Add it' }).at(-1)!;

    await fireEvent.input(typed[typed.length - 1], { target: { value: 'More to it' } });
    await fireEvent.click(add());
    await vi.waitFor(() => expect(sent).toHaveLength(1));

    await fireEvent.input(typed[typed.length - 1], { target: { value: 'And again' } });
    await fireEvent.click(box);
    await fireEvent.click(add());
    await vi.waitFor(() => expect(sent).toHaveLength(2));

    expect(sent[0]).toEqual({ verb: 'addto', id: 'i1', project: 'p', text: 'More to it', refine: true });
    expect(sent[1]).toEqual({ verb: 'addto', id: 'i1', project: 'p', text: 'And again' });
  });

  it('offers removal only on a note no round has read, and marks a read one read', async () => {
    render(Ideas, { rest: ['p', 'i1'] });

    await vi.waitFor(() => expect(screen.getAllByText('Not read yet').length).toBeGreaterThan(0));

    expect(screen.queryAllByRole('button', { name: 'Remove N1' })).toHaveLength(0);
    expect(screen.getAllByRole('button', { name: 'Remove N2' }).length).toBeGreaterThan(0);

    const read = screen.getAllByText('Read already').at(-1)!.closest('li')!;
    expect(read.textContent).toContain('read by a round');
  });
});
