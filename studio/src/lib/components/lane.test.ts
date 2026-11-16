// Notifications in a working view: a view with a terminal or a split in it,
// where a notification over the page covers a pane. There they live in a
// lane of their own under the mode bar, count what is waiting, and open a
// card only when asked; a session's Needs you is marked on its pane too.
// jsdom draws no boxes, so 'covers nothing' is proved in two halves: every
// notification element is inside the lane, and the stylesheets give the lane
// a row of its own that every window-sized container makes room for.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { tick } from 'svelte';
import { readFileSync } from 'node:fs';
import Toasts from './Toasts.svelte';
import { ToastStack, noticesWorking, type ToastSubject } from '../toasts.svelte';
import { subjectOf } from '../state.svelte';
import type { Work } from '../lines';

class FakeTerminal {
  cols = 80;
  rows = 24;
  options: Record<string, unknown> = {};
  textarea = document.createElement('textarea');
  loadAddon() {}
  open() {}
  onData() { return { dispose() {} }; }
  onResize() { return { dispose() {} }; }
  onSelectionChange() { return { dispose() {} }; }
  attachCustomKeyEventHandler() {}
  hasSelection() { return false; }
  write() {}
  focus() {}
  dispose() {}
}

vi.mock('@xterm/xterm', () => ({ Terminal: FakeTerminal }));
vi.mock('@xterm/addon-fit', () => ({ FitAddon: class { fit() {} proposeDimensions() { return { cols: 80, rows: 24 }; } } }));
vi.mock('@xterm/addon-webgl', () => ({ WebglAddon: class { onContextLoss() {} dispose() {} } }));
vi.mock('@xterm/addon-search', () => ({ SearchAddon: class { findNext() { return false; } findPrevious() { return false; } clearDecorations() {} } }));
vi.mock('@xterm/xterm/css/xterm.css', () => ({}));

window.matchMedia ??= ((query: string) => ({ matches: false, media: query, addEventListener() {}, removeEventListener() {} })) as unknown as typeof window.matchMedia;
globalThis.ResizeObserver ??= class { observe() {} disconnect() {} } as unknown as typeof ResizeObserver;

const session = (ref: string): ToastSubject => ({ kind: 'session', ref });

/** Every element that shows a notification, whichever way it is drawn. */
const NOTICE = '.ls-toast, .ls-toast-chip, .ls-toast-past, .ls-toast-history, .ls-lane-badge';

afterEach(() => document.documentElement.removeAttribute('data-lane'));

describe('the stack in a working view', () => {
  it('lets nothing open as a card by itself, however long it waits, and opens what needs you on leaving', () => {
    const stack = new ToastStack();
    stack.tuck(true);

    stack.add({ level: 'needs', text: 'writer/2 asks: delete the branch?', subject: session('s1') });
    stack.add({ level: 'problem', text: 'round 3 failed' });

    for (let second = 0; second < 30; second++) {
      stack.tick();
    }

    expect(stack.list.map((one) => one.folded)).toEqual([true, true]);

    stack.tuck(false);
    expect(stack.list.map((one) => one.folded)).toEqual([false, false]);
  });

  it('counts each level as it shows, an answered question as Done', () => {
    const stack = new ToastStack();
    const asked = stack.add({ level: 'needs', text: 'a asks' });
    stack.add({ level: 'needs', text: 'b asks' });
    stack.add({ level: 'problem', text: 'c failed' });
    stack.add({ level: 'done', text: 'Saved' });

    expect(stack.counts).toEqual({ done: 1, info: 0, needs: 2, problem: 1 });

    stack.settle(asked, 'Answered.');
    expect(stack.counts).toEqual({ done: 2, info: 0, needs: 1, problem: 1 });
  });

  it('says what is open about one session, a Problem first, and nothing once it is answered', () => {
    const stack = new ToastStack();
    const asked = stack.add({ level: 'needs', text: 's1 is waiting for you', subject: session('s1') });
    stack.add({ level: 'needs', text: 's2 is waiting for you', subject: session('s2') });
    stack.add({ level: 'done', text: 's1 said something', subject: session('s1') });

    expect(stack.about('session', 's1')).toEqual({ level: 'needs', count: 1 });
    expect(stack.about('run', 's1')).toBeNull();

    stack.add({ level: 'problem', text: 's1 ended: exit 3', subject: session('s1') });
    expect(stack.about('session', 's1')).toEqual({ level: 'problem', count: 2 });

    stack.settle(asked, 'Answered.');
    expect(stack.about('session', 's1')).toEqual({ level: 'problem', count: 1 });
    expect(stack.about('session', 's3')).toBeNull();
  });

  it('reads the setting as strip unless it names another way', () => {
    expect(noticesWorking(undefined)).toBe('strip');
    expect(noticesWorking('cards')).toBe('strip');
    expect(noticesWorking('chips')).toBe('chips');
    expect(noticesWorking('off')).toBe('off');
  });

  it('gives a toast about a session or a run its subject, and none to anything else', () => {
    expect(subjectOf({ kind: 'session', ref: 'abc' } as Work)).toEqual({ kind: 'session', ref: 'abc' });
    expect(subjectOf({ kind: 'team run', ref: '20261116-0900-aaaa' } as Work)).toEqual({ kind: 'run', ref: '20261116-0900-aaaa' });
    expect(subjectOf({ kind: 'idea', ref: 'tidy-up' } as Work)).toBeUndefined();
  });
});

describe('the lane', () => {
  it('draws every notification inside the lane, and reserves its row while it is there', async () => {
    const stack = new ToastStack();
    stack.add({ level: 'done', text: 'Saved' });
    stack.dismiss(stack.list[0].id);
    stack.add({ level: 'needs', text: 'writer/2 asks: delete the branch?', actions: [{ label: 'Yes', cmd: '' }] });
    stack.add({ level: 'problem', text: 'round 3 failed' });
    const { container, unmount } = render(Toasts, { stack, clock: false, place: 'working' });
    await tick();

    expect(container.querySelector('.ls-toasts')).toBeNull();
    const lane = container.querySelector('.ls-lane')!;
    const shown = Array.from(container.querySelectorAll(NOTICE));
    expect(shown.length).toBeGreaterThan(0);
    expect(shown.filter((one) => !lane.contains(one))).toEqual([]);
    expect(document.documentElement.hasAttribute('data-lane')).toBe(true);

    unmount();
    expect(document.documentElement.hasAttribute('data-lane')).toBe(false);
  });

  it('shows a Needs you as a counted badge, opens its cards only when pressed, and closes them on Escape', async () => {
    const stack = new ToastStack();
    render(Toasts, { stack, clock: false, place: 'working' });
    stack.add({ level: 'needs', text: 'writer/2 asks: delete the branch?', actions: [{ label: 'Yes', cmd: '' }] });
    stack.add({ level: 'needs', text: 'abc is waiting for you' });
    await tick();

    for (let second = 0; second < 15; second++) {
      stack.tick();
    }
    await tick();

    const lane = screen.getByRole('region', { name: 'Notifications' });
    expect(lane.querySelectorAll('.ls-toast')).toHaveLength(0);

    const badge = within(lane).getByRole('button', { name: 'Needs you: 2. Show them' });
    await fireEvent.click(badge);
    expect(lane.querySelectorAll('.ls-toast.needs')).toHaveLength(2);
    expect(within(lane).getByRole('button', { name: 'Needs you: 2. Hide them' }).getAttribute('aria-expanded')).toBe('true');

    await fireEvent.keyDown(lane.querySelector('.ls-toast')!, { key: 'Escape' });
    expect(lane.querySelectorAll('.ls-toast')).toHaveLength(0);
  });

  it('opens a waiting card again as a card once you leave the view', async () => {
    const stack = new ToastStack();
    const { container, rerender } = render(Toasts, { stack, clock: false, place: 'working' });
    stack.add({ level: 'needs', text: 'abc is waiting for you' });
    await tick();
    expect(container.querySelectorAll('.ls-toast')).toHaveLength(0);

    await rerender({ stack, clock: false, place: null });
    expect(container.querySelector('.ls-lane')).toBeNull();
    expect(container.querySelectorAll('.ls-toasts .ls-toast.needs')).toHaveLength(1);
    expect(document.documentElement.hasAttribute('data-lane')).toBe(false);
  });

  it('folds what Ctrl+Shift+X finds open in the lane, then clears what is not waiting', async () => {
    const stack = new ToastStack();
    const { container } = render(Toasts, { stack, clock: false, place: 'working' });
    stack.add({ level: 'done', text: 'Saved' });
    stack.add({ level: 'needs', text: 'abc is waiting for you' });
    await tick();

    await fireEvent.click(screen.getByRole('button', { name: 'Done: 1. Show them' }));
    expect(container.querySelectorAll('.ls-toast')).toHaveLength(1);

    await fireEvent.keyDown(window, { ctrlKey: true, shiftKey: true, code: 'KeyX' });
    expect(container.querySelectorAll('.ls-toast')).toHaveLength(0);
    expect(stack.list).toHaveLength(2);

    await fireEvent.keyDown(window, { ctrlKey: true, shiftKey: true, code: 'KeyX' });
    expect(stack.list.map((one) => one.level)).toEqual(['needs']);
  });

  it('shows the newest as a chip under strip, and only the counts under off', async () => {
    const stack = new ToastStack();
    stack.add({ level: 'info', text: 'Pulled starstats' });
    const { container, rerender } = render(Toasts, { stack, clock: false, place: 'working', notices: 'strip' });
    await tick();
    expect(container.querySelector('.ls-lane .newest .ls-toast-chip')?.textContent).toContain('Pulled starstats');

    await rerender({ stack, clock: false, place: 'working', notices: 'off' });
    expect(container.querySelector('.ls-lane .ls-toast-chip')).toBeNull();
    expect(screen.getByRole('button', { name: 'For your information: 1. Show them' })).toBeTruthy();
  });

  it('keeps the floating chips and reserves nothing under chips', async () => {
    const stack = new ToastStack();
    stack.add({ level: 'needs', text: 'abc is waiting for you' });
    const { container } = render(Toasts, { stack, clock: false, place: 'working', notices: 'chips' });
    await tick();

    expect(container.querySelector('.ls-lane')).toBeNull();
    expect(container.querySelectorAll('.ls-toasts .ls-toast-chip.needs')).toHaveLength(1);
    expect(container.querySelectorAll('.ls-toast')).toHaveLength(0);
    expect(document.documentElement.hasAttribute('data-lane')).toBe(false);
  });

  it('still reads a Problem out at once although no card opens', async () => {
    const stack = new ToastStack();
    render(Toasts, { stack, clock: false, place: 'working' });
    stack.add({ level: 'problem', text: 'round 3 failed' });
    await tick();

    expect(screen.getByRole('alert').textContent).toBe('Problem: round 3 failed');
  });
});

describe('the mark on a pane', () => {
  it('shows a Needs you about its session on the bar, and nothing once it is answered', async () => {
    const { toasts } = await import('../toasts.svelte');
    const { default: SessionPane } = await import('./SessionPane.svelte');
    const asked = toasts.add({ level: 'needs', text: 'abc is waiting for you', subject: session('abc') });
    toasts.add({ level: 'needs', text: 'def is waiting for you', subject: session('def') });
    const { container } = render(SessionPane, { id: 'abc' });
    await tick();

    const mark = container.querySelector('.ls-term .tbar [data-mark]');
    expect(mark?.getAttribute('data-mark')).toBe('needs');
    expect(mark?.textContent).toContain('Needs you');

    toasts.settle(asked, 'Answered.');
    await tick();
    expect(container.querySelector('.ls-term .tbar [data-mark]')).toBeNull();
    toasts.list = [];
  });
});

// The stylesheets, read as the stacking test reads them: Studio's tests load
// no CSS, so the rules themselves are what is checked.
const SHEETS = ['src/styles/components.css', 'src/styles/pages.css', 'src/styles/studio.css'];
const strip = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '');
const rules = () =>
  SHEETS.flatMap((sheet) => [...strip(readFileSync(sheet, 'utf-8')).matchAll(/([^{}@]+)\{([^{}]*)\}/g)].map(([, selector, body]) => ({ selector: selector.trim(), body })));
const declared = (selector: string, property: string) =>
  rules()
    .filter((rule) => rule.selector.split(',').map((one) => one.trim()).includes(selector))
    .flatMap((rule) => [...rule.body.matchAll(new RegExp(`(?:^|;)\\s*${property}\\s*:\\s*([^;]+)`, 'g'))].map(([, value]) => value.trim()));

describe('the room the lane takes', () => {
  it('is a fixed row of its own, directly under the mode bar', () => {
    expect(declared('.ls-lane', 'position')).toEqual(['fixed']);
    expect(declared('.ls-lane', 'top')).toEqual(['var(--ls-focus-top)']);
    expect(declared('.ls-lane', 'height')).toEqual(['var(--ls-lane-h)']);
    expect(declared(':root', '--ls-lane-h')).toHaveLength(1);
  });

  it('moves the page below it, and every container sized to the window gives up its height', () => {
    expect(declared('[data-lane] .page', 'padding-top')).toEqual(['calc(76px + var(--ls-lane-h))']);

    // Each terminal or split container that measures itself against the
    // window, with what it is without the lane.
    for (const [selector, property] of [
      ['.deckwrap', 'height'],
      ['.deskwrap', 'height'],
      ['.tfocus .bpanel', 'top'],
      ['.tfocus .bpanel', 'max-height'],
      ['.ls-beside', 'top'],
      ['.ls-beside', 'max-height'],
    ]) {
      // Sized against the window, that is: 'none' below a breakpoint is not.
      const without = declared(selector, property).filter((value) => /px|vh/.test(value));
      const within = declared(`[data-lane] ${selector}`, property);

      expect(without, `${selector} ${property}`).toHaveLength(1);
      expect(within, `[data-lane] ${selector} ${property}`).toHaveLength(1);
      expect(within[0], `[data-lane] ${selector} ${property}`).toContain('var(--ls-lane-h)');
    }
  });

  it('leaves the board panel alone where it is part of the page rather than sticky', () => {
    const css = strip(readFileSync('src/styles/studio.css', 'utf-8'));

    expect(css).toMatch(/@media \(max-width: 1099px\) \{[^@]*\.tfocus \.bpanel \{ position: static; max-height: none; \}/);
    expect(css).toMatch(/@media \(min-width: 1100px\) \{ \[data-lane\] \.tfocus \.bpanel \{/);
  });
});
