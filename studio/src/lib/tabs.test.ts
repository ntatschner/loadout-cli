// How this browser arranges Terminal Focus's tabs: its own order, the
// running ones it hid and the ended ones it cleared, and letting go of ids
// the daemon no longer has.
import { describe, expect, it } from 'vitest';
import { arrange, clear, hide, nudge, place, prune, readTabs, unhide, withOrder, NO_TABS } from './tabs';

const sessions = [
  { id: 'a', ended: false },
  { id: 'b', ended: false },
  { id: 'c', ended: true },
  { id: 'd', ended: false },
];

const ids = (list: { id: string }[]) => list.map((one) => one.id);

describe('tabs', () => {
  it('keeps the order put in, and puts sessions it has never seen at the end in the daemon order', () => {
    const { shown } = arrange(sessions, { ...NO_TABS, order: ['d', 'b', 'gone'] });

    expect(ids(shown)).toEqual(['d', 'b', 'a', 'c']);
  });

  it('takes hidden tabs off the strip and offers them back, and drops cleared ended ones', () => {
    const { shown, hidden } = arrange(sessions, { order: [], hidden: ['b'], cleared: ['c'] });

    expect(ids(shown)).toEqual(['a', 'd']);
    expect(ids(hidden)).toEqual(['b']);
  });

  it('clears only a tab whose session has ended', () => {
    const { shown } = arrange(sessions, { ...NO_TABS, cleared: ['a'] });

    expect(ids(shown)).toContain('a');
  });

  it('lets go of ids the daemon no longer has', () => {
    expect(prune({ order: ['a', 'x'], hidden: ['y', 'b'], cleared: ['c', 'z'] }, ['a', 'b', 'c'])).toEqual({ order: ['a'], hidden: ['b'], cleared: ['c'] });
  });

  it('puts a dragged tab before another, or at the end', () => {
    expect(place(['a', 'b', 'c'], 'c', 'a')).toEqual(['c', 'a', 'b']);
    expect(place(['a', 'b', 'c'], 'a', null)).toEqual(['b', 'c', 'a']);
  });

  it('moves a tab a step at a time, and no further than either end', () => {
    expect(nudge(['a', 'b', 'c'], 'a', 1)).toEqual(['b', 'a', 'c']);
    expect(nudge(['a', 'b', 'c'], 'c', 1)).toEqual(['a', 'b', 'c']);
    expect(nudge(['a', 'b', 'c'], 'a', -1)).toEqual(['a', 'b', 'c']);
  });

  it('keeps hidden tabs in the order when the strip is reordered', () => {
    const state = withOrder({ order: ['a', 'b', 'd'], hidden: ['b'], cleared: [] }, ['d', 'a']);

    expect(state.order).toEqual(['d', 'a', 'b']);
  });

  it('hides, shows again and clears, each once', () => {
    let state = hide(hide(NO_TABS, 'a'), 'a');
    expect(state.hidden).toEqual(['a']);

    state = unhide(state, 'a');
    expect(state.hidden).toEqual([]);

    state = clear(clear(hide(state, 'c'), 'c'), 'c');
    expect(state).toEqual({ order: [], hidden: [], cleared: ['c'] });
  });

  it('reads nothing arranged from what it cannot read', () => {
    expect(readTabs('')).toEqual(NO_TABS);
    expect(readTabs('{"order":"a","hidden":[1,"b"]}')).toEqual({ order: [], hidden: ['b'], cleared: [] });
  });
});
