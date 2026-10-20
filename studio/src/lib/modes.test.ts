// The modes without a page: which comes next, which panes a layout shows,
// Terminal Focus's keys, Ctrl+Shift+M, how morph() moves under each motion
// setting, and the mode kept in this browser across a reload.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { deckKey, isModeKey, morph, nextMode, placeSlots } from './modes';
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
