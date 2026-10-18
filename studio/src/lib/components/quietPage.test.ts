// QuietPage and QuietList on the six pages: Show everything is built the
// first time and kept; a deep link opens it at once; with nothing to show
// the list is one calm line; and what has gone wrong rises with what needs
// you.
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import QuietPage from './QuietPage.svelte';
import QuietList from './QuietList.svelte';

function counted() {
  const made = { count: 0 };
  const everything = createRawSnippet(() => ({
    render: () => '<div class="page">the whole page</div>',
    setup: () => {
      made.count++;
    },
  }));

  return { made, everything };
}

describe('QuietPage', () => {
  it('builds Show everything the first time it opens, and keeps it', async () => {
    const { made, everything } = counted();
    const shown = render(QuietPage, { title: 'Projects', items: [], everything });

    expect(made.count).toBe(0);
    const more = screen.getByRole('button', { name: 'Show everything' });
    await fireEvent.click(more);
    expect(made.count).toBe(1);
    expect(more.getAttribute('aria-expanded')).toBe('true');

    await fireEvent.click(screen.getByRole('button', { name: 'Show less' }));
    expect((shown.container.querySelector('.all') as HTMLElement).hidden).toBe(true);
    await fireEvent.click(screen.getByRole('button', { name: 'Show everything' }));
    expect(made.count).toBe(1);
  });

  it('opens with everything shown for a deep link', () => {
    const { made, everything } = counted();
    render(QuietPage, { title: 'Memory', items: [], everything, open: true });

    expect(made.count).toBe(1);
    expect(screen.getByRole('button', { name: 'Show less' }).getAttribute('aria-expanded')).toBe('true');
  });

  it('says so calmly when nothing needs you', () => {
    const { everything } = counted();
    render(QuietPage, { title: 'Settings', items: [], everything, calmText: 'Nothing on this machine needs putting right.' });

    expect(screen.getByText('✓ Nothing on this machine needs putting right.')).toBeTruthy();
    expect(screen.queryByRole('list')).toBeNull();
  });
});

describe('QuietList', () => {
  it('puts what has gone wrong with what needs you, at the top', () => {
    const shown = render(QuietList, {
      items: [
        { id: 'a', kind: 'idea', title: 'Shaping one', word: 'shaping' },
        { id: 'b', kind: 'task', title: 'Blocked one', word: 'blocked', ask: 'Waiting on access' },
        { id: 'c', kind: 'finding', title: 'Fixable one', word: 'to put right' },
        { id: 'd', kind: 'idea', title: 'Failed one', word: 'last round failed' },
      ],
    });

    const rows = [...shown.container.querySelectorAll('li:not(.zone)')];
    expect(rows.map((row) => row.querySelector('.t')?.textContent)).toEqual(['Blocked one', 'Fixable one', 'Failed one', 'Shaping one']);
    expect(rows.slice(0, 3).every((row) => row.classList.contains('needs'))).toBe(true);
    expect(screen.getByText('Waiting on access')).toBeTruthy();
  });
});
