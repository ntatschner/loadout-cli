// PageBeside and BesideGrid: Escape closes the page, but never from a
// field, a terminal or a dialog; closing goes back to the last route that
// was not one of the six pages; and the watched side is the same element,
// never made again, whether a page is beside it or not.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import PageBeside from './PageBeside.svelte';
import BesideGrid from './BesideGrid.svelte';
import { besideReturn } from '../modes';

// The dividers watch their grid's size, which jsdom has no layout to give.
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;

const body = createRawSnippet(() => ({ render: () => '<p>Memory page</p>' }));

describe('PageBeside', () => {
  it('closes on Escape and on Close', async () => {
    let closed = 0;
    render(PageBeside, { page: 'Memory', beside: 'your terminals', onclose: () => closed++, children: body });

    expect(screen.getByRole('complementary', { name: 'Memory, beside your terminals' })).toBeTruthy();
    await fireEvent.keyDown(document.body, { key: 'Escape' });
    await fireEvent.click(screen.getByRole('button', { name: /Close/ }));

    expect(closed).toBe(2);
  });

  it('leaves Escape alone in a field, a terminal and a dialog', async () => {
    let closed = 0;
    render(PageBeside, { page: 'Memory', beside: 'your terminals', onclose: () => closed++, children: body });

    const field = document.body.appendChild(document.createElement('input'));
    const terminal = document.body.appendChild(document.createElement('div'));
    terminal.className = 'xterm';
    const inside = terminal.appendChild(document.createElement('textarea'));
    const dialog = document.body.appendChild(document.createElement('div'));
    dialog.className = 'ls-dialog';
    const button = dialog.appendChild(document.createElement('button'));

    await fireEvent.keyDown(field, { key: 'Escape' });
    await fireEvent.keyDown(inside, { key: 'Escape' });
    await fireEvent.keyDown(button, { key: 'Escape' });
    expect(closed).toBe(0);

    // With the dialog gone, Escape from the page closes it.
    dialog.remove();
    await fireEvent.keyDown(document.body, { key: 'Escape' });
    expect(closed).toBe(1);

    field.remove();
    terminal.remove();
  });
});

describe('besideReturn', () => {
  it('goes back to the last route that was not one of the six pages, or Home', () => {
    let kept = ['home'];

    kept = besideReturn(kept, { page: 'session', rest: ['s1'] });
    kept = besideReturn(kept, { page: 'memory', rest: ['loadout-cli', 'project', 'x'] });
    kept = besideReturn(kept, { page: 'spend', rest: [] });

    expect(kept).toEqual(['session', 's1']);
    expect(besideReturn(['home'], { page: 'ideas', rest: [] })).toEqual(['home']);
  });

  it('passes over anything else the mode opened beside, a run beside the deck or a session beside the board', () => {
    expect(besideReturn(['session', 's1'], { page: 'teams', rest: ['r1'] }, true)).toEqual(['session', 's1']);
    expect(besideReturn(['teams', 'r1'], { page: 'session', rest: ['s2'] }, true)).toEqual(['teams', 'r1']);
    expect(besideReturn(['home'], { page: 'teams', rest: ['r1'] })).toEqual(['teams', 'r1']);
  });
});

describe('BesideGrid', () => {
  it('keeps the watched side the same element when a page opens and closes beside it', async () => {
    let made = 0;
    const focus = createRawSnippet(() => ({
      render: () => '<div class="deck">terminals</div>',
      setup: () => {
        made++;
      },
    }));
    const beside = createRawSnippet(() => ({ render: () => '<aside class="ls-beside">a page</aside>' }));

    const shown = render(BesideGrid, { focus });
    const deck = shown.container.querySelector('.deck');

    await shown.rerender({ focus, beside });
    expect(shown.container.querySelector('.ls-beside')).toBeTruthy();
    await shown.rerender({ focus, beside: undefined });
    expect(shown.container.querySelector('.ls-beside')).toBeNull();

    expect(made).toBe(1);
    expect(shown.container.querySelector('.deck')).toBe(deck);
  });
});

describe('BesideGrid divider', () => {
  const focus = createRawSnippet(() => ({ render: () => '<div class="deck">terminals</div>' }));
  const beside = createRawSnippet(() => ({ render: () => '<aside class="ls-beside">a page</aside>' }));
  const grid = () => document.querySelector<HTMLElement>('.ls-besidegrid')!;

  it('has a divider only while a page is beside, and leaves the stylesheet alone until it is moved', () => {
    const shown = render(BesideGrid, { focus });
    expect(screen.queryByRole('separator')).toBeNull();

    shown.unmount();
    render(BesideGrid, { focus, beside });
    expect(screen.getByRole('separator', { name: 'Width of the page beside' }).getAttribute('aria-valuenow')).toBe('48');
    expect(grid().style.getPropertyValue('--beside-cols')).toBe('');
  });

  it('sizes the page beside from where it was left, and reports how much it has after a move', async () => {
    const onsplit = vi.fn();
    render(BesideGrid, { focus, beside, split: 0.4, onsplit });

    expect(grid().style.getPropertyValue('--beside-cols')).toBe('minmax(0, 0.6000fr) minmax(0, 0.4000fr)');
    await fireEvent.keyDown(screen.getByRole('separator'), { key: 'ArrowLeft' });

    expect(onsplit.mock.lastCall![0]).toBeCloseTo(0.42);
  });
});
