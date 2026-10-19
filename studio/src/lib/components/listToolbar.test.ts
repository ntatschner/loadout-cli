// ListToolbar: every control edits the one query string the daemon read, so
// a chip, a menu and the box can never say different things.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import ListToolbar from './ListToolbar.svelte';
import { exampleQuery, partSaid, sortOf, withoutPart, withSort, withTerm, type ListShape } from '../lists';
import { queryWords } from '../taskList.svelte';

const SHAPE: ListShape = {
  text: '-state:done,dropped login sort:-updated',
  parts: ['-state:done,dropped', 'login', 'sort:-updated'],
  fields: [
    { name: 'id', type: 'text', description: 'the id', choices: [] },
    { name: 'state', type: 'choice', description: 'where it stands', choices: ['open', 'doing', 'blocked', 'done', 'dropped'] },
    { name: 'updated', type: 'date', description: 'when', choices: [] },
  ],
  defaultQuery: '-state:done,dropped',
};

function toolbar(extra: Record<string, unknown> = {}) {
  const sent: string[] = [];
  render(ListToolbar, { noun: 'tasks', query: SHAPE.text, shape: SHAPE, onchange: (query: string) => sent.push(query), ...extra });

  return sent;
}

describe('list query helpers', () => {
  it('edit the pieces and nothing else', () => {
    expect(withoutPart(SHAPE.parts, 1)).toBe('-state:done,dropped sort:-updated');
    expect(withTerm(SHAPE.parts, 'state:open')).toBe('-state:done,dropped login sort:-updated state:open');
    expect(withTerm(SHAPE.parts, 'login')).toBe(SHAPE.text);
    expect(sortOf(SHAPE.parts)).toBe('-updated');
    expect(withSort(SHAPE.parts, 'id')).toBe('-state:done,dropped login sort:id');
    expect(withSort(SHAPE.parts, '')).toBe('-state:done,dropped login');
    expect(partSaid('sort:-updated,id')).toBe('sorted by updated, descending, then id');
  });

  it("offer an example from the list's own fields, never one it would refuse", () => {
    expect(exampleQuery(SHAPE.fields)).toBe('state:open sort:-updated');
    expect(exampleQuery([{ name: 'agent', type: 'text', description: 'which agent', choices: [] }, { name: 'active', type: 'date', description: 'when', choices: [] }])).toBe('sort:-active');
    expect(exampleQuery([])).toBe('');
  });

  it('send the default, everything and a query as the command spells them', () => {
    expect(queryWords(null)).toEqual([]);
    expect(queryWords('')).toEqual(['--all']);
    expect(queryWords('-state:open')).toEqual(['--filter=-state:open']);
  });
});

describe('ListToolbar', () => {
  it("suggests a query in the list's own fields", () => {
    render(ListToolbar, {
      noun: 'conversations',
      query: '',
      shape: { text: '', parts: [], defaultQuery: '', fields: [{ name: 'active', type: 'date', description: 'when', choices: [] }] },
      onchange: () => undefined,
    });

    expect(screen.getByRole('searchbox').getAttribute('placeholder')).toBe('Type to filter, or sort:-active');
  });

  it('shows each piece as a chip, and removing one sends the rest', async () => {
    const sent = toolbar();

    await fireEvent.click(screen.getByRole('button', { name: 'Remove login' }));

    expect(sent).toEqual(['-state:done,dropped sort:-updated']);
  });

  it('adds a filter and chooses a sort from its menus', async () => {
    const sent = toolbar();

    await fireEvent.change(screen.getByRole('combobox', { name: 'Add a filter' }), { target: { value: 'state:blocked' } });
    await fireEvent.change(screen.getByRole('combobox', { name: 'Sort by' }), { target: { value: 'id' } });

    expect(sent).toEqual(['-state:done,dropped login sort:-updated state:blocked', '-state:done,dropped login sort:id']);
  });

  it('sends what is typed on Enter, or after a pause', async () => {
    vi.useFakeTimers();

    try {
      const sent = toolbar();
      const box = screen.getByRole('searchbox', { name: 'Find tasks' });

      await fireEvent.input(box, { target: { value: 'state:open' } });
      expect(sent).toEqual([]);
      vi.advanceTimersByTime(400);
      expect(sent).toEqual(['state:open']);

      await fireEvent.input(box, { target: { value: 'state:doing' } });
      await fireEvent.keyDown(box, { key: 'Enter' });
      vi.advanceTimersByTime(400);
      expect(sent).toEqual(['state:open', 'state:doing']);
    } finally {
      vi.useRealTimers();
    }
  });

  it('puts you in the box on / unless you are typing somewhere already', async () => {
    toolbar();
    const box = screen.getByRole('searchbox', { name: 'Find tasks' });

    await fireEvent.keyDown(document.body, { key: '/' });

    expect(document.activeElement).toBe(box);
  });

  it('offers a reset only away from the default, and says how much the filter hides', async () => {
    const sent = toolbar({ shown: 0, recorded: 4 });

    expect(screen.getByRole('status').textContent).toContain('Showing 0 of 4. Nothing matches.');
    await fireEvent.click(screen.getByRole('button', { name: 'Show all 4' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Reset to default' }));

    expect(sent).toEqual(['', '-state:done,dropped']);
  });

  it('offers no reset at the default', () => {
    render(ListToolbar, { noun: 'tasks', query: SHAPE.defaultQuery, shape: { ...SHAPE, text: SHAPE.defaultQuery, parts: [SHAPE.defaultQuery] }, onchange: () => undefined });

    expect(screen.queryByRole('button', { name: 'Reset to default' })).toBeNull();
  });

  it('says what the daemon refused beside the box', () => {
    toolbar({ problem: "'colour' is not something tasks can be filtered on." });

    expect(screen.getByRole('alert').textContent).toContain("'colour' is not something");
    expect(screen.getByRole('searchbox', { name: 'Find tasks' }).getAttribute('aria-invalid')).toBe('true');
  });
});
