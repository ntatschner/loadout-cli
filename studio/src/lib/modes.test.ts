// The modes without a page: which comes next, which panes a layout shows,
// Terminal Focus's keys, Ctrl+Shift+M, how morph() moves under each motion
// setting, and the mode kept in this browser across a reload.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { deckKey, isModeKey, launchSlots, morph, nextMode, placeSlots, shrinkSlots } from './modes';
import { Studio } from './state.svelte';

const key = (over: Partial<KeyboardEvent>) => ({ ctrlKey: false, shiftKey: false, altKey: false, metaKey: false, key: '', code: '', ...over });

describe('the modes', () => {
  it('cycle Default, Terminals, Teams, Minimal, Pure and round again', () => {
    expect(nextMode('default')).toBe('terminals');
    expect(nextMode('terminals')).toBe('teams');
    expect(nextMode('teams')).toBe('minimal');
    expect(nextMode('minimal')).toBe('pure');
    expect(nextMode('pure')).toBe('default');
  });

  it('cycle on Ctrl+Shift+M and on nothing short of it', () => {
    expect(isModeKey(key({ ctrlKey: true, shiftKey: true, key: 'M', code: 'KeyM' }))).toBe(true);
    expect(isModeKey(key({ ctrlKey: true, key: 'm', code: 'KeyM' }))).toBe(false);
    expect(isModeKey(key({ shiftKey: true, key: 'M', code: 'KeyM' }))).toBe(false);
    expect(isModeKey(key({ ctrlKey: true, shiftKey: true, altKey: true, key: 'M', code: 'KeyM' }))).toBe(false);
  });
});

describe('Terminal Focus', () => {
  const order = ['a', 'b', 'c', 'd', 'e'];

  it('fills empty panes in tab order, around a session placed first', () => {
    expect(placeSlots([], { order, layout: 1, pick: 'd' })).toEqual(['d']);
    expect(placeSlots([], { order, layout: 2, pick: 'd' })).toEqual(['d', 'a']);
    expect(placeSlots([], { order, layout: 4 })).toEqual(['a', 'b', 'c', 'd']);
    expect(placeSlots([], { order: ['a'], layout: 2 })).toEqual(['a', null]);
  });

  it('changes nothing when the pick is already in a pane', () => {
    expect(placeSlots(['a', 'b', 'c', 'd'], { order, layout: 4, pick: 'c', at: 0 })).toEqual(['a', 'b', 'c', 'd']);
  });

  it('puts a pick from off the screen in the highlighted pane, moving no other', () => {
    expect(placeSlots(['a', 'b', 'c', 'd'], { order, layout: 4, pick: 'e', at: 2 })).toEqual(['a', 'b', 'e', 'd']);
    // With nothing highlighted, the first empty pane, or else the first.
    expect(placeSlots(['a', null], { order: ['a'], layout: 2, pick: 'x' })).toEqual(['a', 'x']);
    expect(placeSlots(['a', 'b'], { order, layout: 2, pick: 'e' })).toEqual(['e', 'b']);
  });

  it('keeps a session that has ended or gone in its pane', () => {
    expect(placeSlots(['a', 'old'], { order: ['a', 'b'], layout: 2 })).toEqual(['a', 'old']);
    expect(placeSlots(['a', 'old'], { order: ['a', 'b'], layout: 2, pick: 'a', at: 0 })).toEqual(['a', 'old']);
  });

  it('drops panes from the end when the layout shrinks, though the pick keeps the last', () => {
    expect(placeSlots(['a', 'b', 'c', 'd'], { order, layout: 2, pick: 'a', at: 0 })).toEqual(['a', 'b']);
    expect(placeSlots(['a', 'b', 'c', 'd'], { order, layout: 2, pick: 'd', at: 3 })).toEqual(['a', 'd']);
    expect(placeSlots(['a', 'b', 'c', 'd'], { order, layout: 1, pick: 'c', at: 2 })).toEqual(['c']);
  });

  it('adds panes in tab order when the layout grows, leaving the others where they are', () => {
    expect(placeSlots(['d', 'b'], { order, layout: 4 })).toEqual(['d', 'b', 'a', 'c']);
  });

  it('reads a damaged arrangement as empty where it repeats itself', () => {
    expect(placeSlots(['a', 'a'], { order, layout: 2 })).toEqual(['a', 'b']);
  });

  it('fills only the panes it is told to from spare tabs, leaving the rest empty', () => {
    expect(placeSlots(['a', 'b', 'c', null], { order, layout: 4, fill: 2 })).toEqual(['a', 'b', 'c', null]);
    expect(placeSlots(['a', null], { order, layout: 2, fill: 2 })).toEqual(['a', 'b']);
  });

  describe('a session just started', () => {
    it('takes an empty pane, not the highlighted one', () => {
      expect(launchSlots(['a', null, 'c', 'd'], { layout: 4, pick: 'n' })).toEqual({ slots: ['a', 'n', 'c', 'd'], layout: 4, at: 1, grew: false, replaced: null, because: null });
    });

    it('grows the layout one step, 1 to 2 to 4, when no pane is free', () => {
      expect(launchSlots(['a'], { layout: 1, pick: 'n' })).toEqual({ slots: ['a', 'n'], layout: 2, at: 1, grew: true, replaced: null, because: null });
      expect(launchSlots(['a', 'b'], { layout: 2, pick: 'n' })).toEqual({ slots: ['a', 'b', 'n', null], layout: 4, at: 2, grew: true, replaced: null, because: null });
    });

    it('with four full, takes the pane of one that has ended before any idle one', () => {
      const placed = launchSlots(['a', 'b', 'c', 'd'], { layout: 4, pick: 'n', ended: ['c'], active: { a: 1, b: 2, c: 3, d: 4 } });

      expect(placed).toEqual({ slots: ['a', 'b', 'n', 'd'], layout: 4, at: 2, grew: false, replaced: 'c', because: 'ended' });
    });

    it('with none ended, takes the pane idle longest, and one never seen active counts as idlest', () => {
      expect(launchSlots(['a', 'b', 'c', 'd'], { layout: 4, pick: 'n', active: { a: 30, b: 10, c: 20, d: 40 } }).replaced).toBe('b');
      expect(launchSlots(['a', 'b', 'c', 'd'], { layout: 4, pick: 'n', active: { a: 30, b: 10, c: 20 } })).toMatchObject({ at: 3, replaced: 'd', because: 'idle' });
    });

    it('never pushes out a session waiting on you, unless every pane is waiting', () => {
      expect(launchSlots(['a', 'b', 'c', 'd'], { layout: 4, pick: 'n', waiting: ['b'], active: { a: 30, b: 10, c: 20, d: 40 } }).replaced).toBe('c');
      expect(launchSlots(['a', 'b', 'c', 'd'], { layout: 4, pick: 'n', waiting: ['a', 'b', 'c', 'd'], active: { a: 30, b: 10, c: 20, d: 40 } }).replaced).toBe('b');
    });

    it('takes the pane of a session gone from the list as an empty one, before growing', () => {
      expect(launchSlots(['old'], { layout: 1, pick: 'n', gone: ['old'] })).toEqual({ slots: ['n'], layout: 1, at: 0, grew: false, replaced: null, because: null });
    });

    it('stays where it is if it is already in a pane', () => {
      expect(launchSlots(['a', 'n'], { layout: 2, pick: 'n' })).toMatchObject({ slots: ['a', 'n'], layout: 2, at: 1, grew: false });
    });
  });

  describe('giving panes back', () => {
    it('shrinks to the smallest layout that holds the sessions still listed, keeping their order', () => {
      expect(shrinkSlots(['a', 'gone', 'n', null], { layout: 4, chosen: 1, order: ['a', 'n'] })).toEqual({ slots: ['a', 'n'], layout: 2 });
      expect(shrinkSlots(['gone', 'n'], { layout: 2, chosen: 1, order: ['n'] })).toEqual({ slots: ['n'], layout: 1 });
    });

    it('never below the layout you chose', () => {
      expect(shrinkSlots(['a', null, null, null], { layout: 4, chosen: 2, order: ['a'] })).toEqual({ slots: ['a', null], layout: 2 });
      expect(shrinkSlots(['a', null, null, null], { layout: 4, chosen: 4, order: ['a'] })).toEqual({ slots: ['a', null, null, null], layout: 4 });
    });

    it('keeps the grown layout while it is still full enough to need it', () => {
      const slots = ['a', 'b', 'n', null];

      expect(shrinkSlots(slots, { layout: 4, chosen: 2, order: ['a', 'b', 'n'] })).toEqual({ slots, layout: 4 });
    });
  });

  it('picks a session by its tab number on Ctrl+Alt, read from the physical key too', () => {
    expect(deckKey(key({ ctrlKey: true, altKey: true, key: '3', code: 'Digit3' }), order, ['a'], 'a')).toBe('c');
    // AltGr layouts turn Ctrl+Alt+2 into another character; the key is still Digit2.
    expect(deckKey(key({ ctrlKey: true, altKey: true, key: '@', code: 'Digit2' }), order, ['a'], 'a')).toBe('b');
    expect(deckKey(key({ ctrlKey: true, altKey: true, key: '9', code: 'Digit9' }), order, ['a'], 'a')).toBeNull();
    expect(deckKey(key({ ctrlKey: true, key: '3', code: 'Digit3' }), order, ['a'], 'a')).toBeNull();
  });

  it('moves between the panes shown on Ctrl+Alt+arrows, round the ends', () => {
    const shown = ['b', 'a', 'c'];

    expect(deckKey(key({ ctrlKey: true, altKey: true, key: 'ArrowRight' }), order, shown, 'b')).toBe('a');
    expect(deckKey(key({ ctrlKey: true, altKey: true, key: 'ArrowLeft' }), order, shown, 'b')).toBe('c');
    expect(deckKey(key({ ctrlKey: true, altKey: true, key: 'ArrowDown' }), order, shown, 'c')).toBe('b');
  });
});

describe('morph()', () => {
  let transition: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    transition = vi.fn((update: () => void) => {
      update();

      return { finished: Promise.resolve(), ready: Promise.resolve() };
    });
    (document as unknown as { startViewTransition?: unknown }).startViewTransition = transition;
  });

  afterEach(() => {
    delete (document as unknown as { startViewTransition?: unknown }).startViewTransition;
    document.documentElement.removeAttribute('data-motion');
    document.documentElement.removeAttribute('data-vt');
    document.body.innerHTML = '';
  });

  it('under no motion makes the change and starts no view transition at all', () => {
    document.documentElement.setAttribute('data-motion', 'none');
    const apply = vi.fn();

    morph(apply);

    expect(apply).toHaveBeenCalledOnce();
    expect(transition).not.toHaveBeenCalled();
  });

  it('under full motion runs the change as a view transition', () => {
    document.documentElement.setAttribute('data-motion', 'full');
    const apply = vi.fn();

    morph(apply);

    expect(transition).toHaveBeenCalledOnce();
    expect(apply).toHaveBeenCalledOnce();
    expect(document.documentElement.getAttribute('data-vt')).toBeNull();
  });

  it('under reduced motion makes the view transition a fade', () => {
    document.documentElement.setAttribute('data-motion', 'reduced');

    morph(() => undefined);

    expect(transition).toHaveBeenCalledOnce();
    expect(document.documentElement.getAttribute('data-vt')).toBe('fade');
  });

  it('where the browser has no view transition, rises the page in, or fades it under reduced motion', () => {
    delete (document as unknown as { startViewTransition?: unknown }).startViewTransition;
    document.body.innerHTML = '<main id="main"></main>';
    const main = document.getElementById('main')!;

    document.documentElement.setAttribute('data-motion', 'full');
    morph(() => undefined);
    expect(main.className).toBe('ls-enter');

    document.documentElement.setAttribute('data-motion', 'reduced');
    morph(() => undefined);
    expect(main.className).toBe('ls-enter-fade');
  });
});

describe('the mode in this browser', () => {
  afterEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-motion');
    document.documentElement.removeAttribute('data-mode');
  });

  it('is kept on change, set on <html>, and read back by the next page load', () => {
    document.documentElement.setAttribute('data-motion', 'none');
    const first = new Studio();

    first.setMode('teams');

    expect(document.documentElement.getAttribute('data-mode')).toBe('teams');
    expect(localStorage.getItem('studio-mode')).toBe('teams');
    expect(new Studio().mode).toBe('teams');
  });

  it('cycles from the one it is in', () => {
    document.documentElement.setAttribute('data-motion', 'none');
    const studio = new Studio();

    studio.setMode('pure');
    studio.cycleMode();

    expect(studio.mode).toBe('default');
  });

  it('starts in Default where nothing, or nothing it knows, is kept', () => {
    expect(new Studio().mode).toBe('default');

    localStorage.setItem('studio-mode', 'sideways');
    expect(new Studio().mode).toBe('default');
  });
});
