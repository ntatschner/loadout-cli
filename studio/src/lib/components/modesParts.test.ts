// The Modes components driven as a person would: the switcher by arrow
// keys, the waiting count going to the oldest question, Terminal Focus's
// keys and panes, the board and the quiet column putting what needs you
// first, and the focus frame handing the keyboard back.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { createRawSnippet, tick } from 'svelte';
import FocusFrame from './FocusFrame.svelte';
import ModeBar from './ModeBar.svelte';
import ModeSwitcher from './ModeSwitcher.svelte';
import QuietList from './QuietList.svelte';
import RunBoard from './RunBoard.svelte';
import TermDeck from './TermDeck.svelte';
import type { DeckSession, PaneHandle } from '../modes';

// The dividers watch their grid's size, which jsdom has no layout to give.
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;

describe('ModeSwitcher', () => {
  it('moves on the arrow keys, Home and End, and says which is picked', async () => {
    const onchange = vi.fn();
    render(ModeSwitcher, { mode: 'teams', onchange });

    const teams = screen.getByRole('radio', { name: /Teams/ });
    expect(teams.getAttribute('aria-checked')).toBe('true');
    expect(teams.getAttribute('tabindex')).toBe('0');

    await fireEvent.keyDown(teams, { key: 'ArrowRight' });
    expect(onchange).toHaveBeenLastCalledWith('minimal');
    await fireEvent.keyDown(teams, { key: 'ArrowLeft' });
    expect(onchange).toHaveBeenLastCalledWith('terminals');
    await fireEvent.keyDown(teams, { key: 'Home' });
    expect(onchange).toHaveBeenLastCalledWith('default');
    await fireEvent.keyDown(teams, { key: 'End' });
    expect(onchange).toHaveBeenLastCalledWith('pure');
  });

  it('does not change to the mode it is already in', async () => {
    const onchange = vi.fn();
    render(ModeSwitcher, { mode: 'default', onchange });

    await fireEvent.click(screen.getByRole('radio', { name: /Default/ }));

    expect(onchange).not.toHaveBeenCalled();
  });
});

describe('ModeBar', () => {
  it('counts sessions and runs together and goes to the oldest question', async () => {
    const onwaiting = vi.fn();
    const waiting = [
      { kind: 'run' as const, id: 'r1', label: 'Merge it?', at: 300 },
      { kind: 'session' as const, id: 's1', label: 'fix-tests needs approval', at: 100 },
    ];
    render(ModeBar, { mode: 'default', waiting, onwaiting });

    const button = screen.getByRole('button', { name: /need you/ });
    expect(button.textContent?.replace(/\s+/g, ' ')).toContain('2 need you · fix-tests needs approval');

    await fireEvent.click(button);
    expect(onwaiting).toHaveBeenCalledWith(waiting[1]);
  });

  it('says so when nothing is waiting', () => {
    render(ModeBar, { mode: 'minimal' });

    expect(screen.getByText('nothing waiting on you')).toBeTruthy();
  });
});

describe('TermDeck', () => {
  const sessions: DeckSession[] = [
    { id: 'a', title: 'fix-tests', word: 'working' },
    { id: 'b', title: 'docs', word: 'needs approval' },
    { id: 'c', title: 'release', word: 'working' },
  ];

  // A pane that only records being on the page and leaving it, in place of
  // the live terminal, whose socket closes as it leaves.
  function deck(props: Record<string, unknown> = {}) {
    const opened: string[] = [];
    const closed: string[] = [];
    const focused: string[] = [];
    const pane = createRawSnippet((id: () => string, register: () => (handle: PaneHandle | null) => void) => ({
      render: () => `<div class="fake" data-pane="${id()}"></div>`,
      setup: () => {
        const mine = id();
        opened.push(mine);
        register()({ focus: () => focused.push(mine), find: () => true, clearFind: () => undefined });

        return () => closed.push(mine);
      },
    }));

    render(TermDeck, { sessions, current: 'a', layout: 1, pane, ...props });

    const shown = () => [...document.querySelectorAll('[data-pane]')].map((node) => node.getAttribute('data-pane'));

    return { opened, closed, focused, shown };
  }

  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('picks a session by Ctrl+Alt and its tab number, and the one it showed leaves', async () => {
    const onpick = vi.fn();
    const { closed, shown } = deck({ onpick });
    expect(shown()).toEqual(['a']);

    await fireEvent.keyDown(screen.getByRole('region', { name: 'Terminals' }), { key: '3', code: 'Digit3', ctrlKey: true, altKey: true });
    await tick();

    expect(onpick).toHaveBeenCalledWith('c');
    expect(shown()).toEqual(['c']);
    expect(closed).toEqual(['a']);
    expect(screen.getByRole('tab', { name: /release/ }).getAttribute('aria-selected')).toBe('true');
  });

  it('keeps the picked session among the panes when the layout changes', async () => {
    const onlayout = vi.fn();
    const { shown } = deck({ current: 'c', onlayout });

    await fireEvent.click(screen.getByRole('radio', { name: 'Two side by side' }));
    await tick();

    expect(onlayout).toHaveBeenCalledWith(2);
    expect(shown()).toEqual(['c', 'a']);
  });

  it('moves between the panes shown on Ctrl+Alt+arrows, putting the keyboard there', async () => {
    const { focused } = deck({ layout: 2 });

    await fireEvent.keyDown(screen.getByRole('region', { name: 'Terminals' }), { key: 'ArrowRight', ctrlKey: true, altKey: true });
    await new Promise((resolve) => setTimeout(resolve, 5));

    expect(focused).toEqual(['b']);
  });

  it('keeps every pane where it is when you click into one, moving only the highlight', async () => {
    const four: DeckSession[] = [...sessions, { id: 'd', title: 'notes', word: 'working' }];
    const onpick = vi.fn();
    const { shown, closed } = deck({ sessions: four, layout: 4, onpick });
    const panes = () => [...document.querySelectorAll<HTMLElement>('.pane')].map((node) => node.dataset.id);
    expect(panes()).toEqual(['a', 'b', 'c', 'd']);

    await fireEvent.focusIn(document.querySelector('.pane[data-id="c"] [data-pane]')!);
    await tick();

    expect(panes()).toEqual(['a', 'b', 'c', 'd']);
    expect(shown()).toEqual(['a', 'b', 'c', 'd']);
    expect(closed).toEqual([]);
    expect(document.querySelector('.pane.on')?.getAttribute('data-id')).toBe('c');
    expect(onpick).toHaveBeenCalledWith('c');
  });

  it('puts a session picked from its tab into the highlighted pane, moving no other', async () => {
    const five: DeckSession[] = [...sessions, { id: 'd', title: 'notes', word: 'working' }, { id: 'e', title: 'spike', word: 'working' }];
    const onslots = vi.fn();
    const { closed } = deck({ sessions: five, layout: 4, onslots });
    const panes = () => [...document.querySelectorAll<HTMLElement>('.pane')].map((node) => node.dataset.id);

    await fireEvent.focusIn(document.querySelector('.pane[data-id="b"] [data-pane]')!);
    await fireEvent.click(screen.getByRole('tab', { name: /spike/ }));
    await tick();

    expect(panes()).toEqual(['a', 'e', 'c', 'd']);
    expect(closed).toEqual(['b']);
    expect(document.querySelector('.pane.on')?.getAttribute('data-id')).toBe('e');
    expect(onslots).toHaveBeenLastCalledWith(['a', 'e', 'c', 'd']);
  });

  it('keeps a session that has gone in its pane, and says it has gone', async () => {
    const { shown } = deck({ layout: 2, slots: ['a', 'old'] });

    expect(shown()).toEqual(['a', 'old']);
    expect(document.querySelector('.pane[data-id="old"]')?.textContent).toContain('no longer in Studio');
    expect(document.querySelector('.pane[data-id="a"]')?.textContent).not.toContain('no longer in Studio');
  });

  it('gives up the pane of an ended tab cleared off the strip, and the next tab takes it', async () => {
    const onclear = vi.fn();
    const onhide = vi.fn();
    const mixed: DeckSession[] = [sessions[0], { ...sessions[1], word: 'ended', ended: true }, sessions[2]];
    deck({ sessions: mixed, layout: 2, changes: false, onclear, onhide });
    const panes = () => [...document.querySelectorAll<HTMLElement>('.pane')].map((node) => node.dataset.id);
    expect(panes()).toEqual(['a', 'b']);

    await fireEvent.click(screen.getByRole('button', { name: 'Close docs' }));
    await tick();

    expect(onclear).toHaveBeenCalledWith('b');
    expect(panes()).toEqual(['a', 'c']);
    expect(onhide).not.toHaveBeenCalled();
  });

  it('gives up the pane of a running tab hidden off the strip, leaving the others in place', async () => {
    const onhide = vi.fn();
    deck({ layout: 2, changes: false, onhide });
    const panes = () => [...document.querySelectorAll<HTMLElement>('.pane')].map((node) => node.dataset.id);
    expect(panes()).toEqual(['a', 'b']);

    await fireEvent.click(screen.getByRole('button', { name: 'Close fix-tests' }));
    await tick();

    expect(onhide).toHaveBeenCalledWith('a');
    expect(panes()).toEqual(['c', 'b']);
  });

  it('offers the one waiting, and goes to it', async () => {
    const { shown } = deck();

    await fireEvent.click(screen.getByRole('button', { name: /Go to the one waiting/ }));
    await tick();

    expect(shown()).toEqual(['b']);
  });

  it('numbers the tabs in the order it is given, so Ctrl+Alt+1 is the first tab as arranged', async () => {
    const onpick = vi.fn();
    deck({ sessions: [sessions[2], sessions[0], sessions[1]], onpick });

    expect(screen.getByRole('tab', { name: /release/ }).getAttribute('aria-keyshortcuts')).toBe('Control+Alt+1');

    await fireEvent.keyDown(screen.getByRole('region', { name: 'Terminals' }), { key: '1', code: 'Digit1', ctrlKey: true, altKey: true });
    expect(onpick).toHaveBeenCalledWith('c');
  });

  it('numbers the tabs with their keys and marks new output', () => {
    deck({ sessions: [{ ...sessions[0] }, { ...sessions[1], unread: true }] });

    expect(screen.getByRole('tab', { name: /fix-tests/ }).getAttribute('aria-keyshortcuts')).toBe('Control+Alt+1');
    expect(screen.getByRole('tab', { name: /docs/ }).textContent).toContain('new output');
  });

  describe('dividers', () => {
    const panesOf = () => document.querySelector<HTMLElement>('.panes')!;
    const width = () => screen.queryByRole('separator', { name: 'Width of the panes' });
    const height = () => screen.queryByRole('separator', { name: 'Height of the panes' });

    it('has none for one pane, one for two, and for four one across and one down, shared by the grid', async () => {
      deck({ layout: 1 });
      expect(screen.queryAllByRole('separator')).toHaveLength(0);

      await fireEvent.click(screen.getByRole('radio', { name: 'Two side by side' }));
      expect(width()).toBeTruthy();
      expect(height()).toBeNull();
      expect(document.querySelector('.ls-deck .keys')!.textContent).toContain('move it · drag a divider');

      await fireEvent.click(screen.getByRole('radio', { name: 'Four' }));
      expect(screen.getAllByRole('separator')).toHaveLength(2);
      expect(width()!.getAttribute('aria-orientation')).toBe('vertical');
      expect(height()!.getAttribute('aria-orientation')).toBe('horizontal');
    });

    it('has none while only one session is there to show', () => {
      deck({ layout: 2, sessions: [sessions[0]] });

      expect(screen.queryAllByRole('separator')).toHaveLength(0);
    });

    it('comes after the panes, so each pane keeps its place in the grid', () => {
      deck({ layout: 4, sessions: [...sessions, { id: 'd', title: 'four', word: 'working' }] });

      const children = [...panesOf().children];
      expect(children.slice(0, 4).every((one) => one.classList.contains('pane'))).toBe(true);
      expect(children.slice(4).map((one) => one.getAttribute('role') ?? one.className)).toEqual(['separator', 'separator', expect.stringContaining('ls-cross')]);
    });

    it('sizes the grid from where they were left, and reports a move for the layout showing', async () => {
      const onsplit = vi.fn();
      deck({ layout: 4, splits: { 2: [0.7], 4: [0.3, 0.6] }, onsplit });

      expect(panesOf().style.getPropertyValue('--deck-cols')).toBe('minmax(0, 0.3000fr) minmax(0, 0.7000fr)');
      expect(panesOf().style.getPropertyValue('--deck-rows')).toBe('minmax(0, 0.6000fr) minmax(0, 0.4000fr)');
      expect(width()!.getAttribute('aria-valuenow')).toBe('30');

      await fireEvent.keyDown(height()!, { key: 'ArrowDown', shiftKey: true });
      expect(onsplit).toHaveBeenLastCalledWith(4, [0.3, 0.7]);
      await tick();
      expect(panesOf().style.getPropertyValue('--deck-rows')).toBe('minmax(0, 0.7000fr) minmax(0, 0.3000fr)');

      // Two panes keep their own place, untouched by four's.
      await fireEvent.click(screen.getByRole('radio', { name: 'Two side by side' }));
      expect(width()!.getAttribute('aria-valuenow')).toBe('70');
      expect(panesOf().style.getPropertyValue('--deck-rows')).toBe('');
      await fireEvent.keyDown(width()!, { key: 'Enter' });
      expect(onsplit).toHaveBeenLastCalledWith(2, [0.5]);
    });

    it('has a crossing that moves both only in four, and reports both', async () => {
      const onsplit = vi.fn();
      deck({ layout: 2, onsplit, sessions: [...sessions, { id: 'd', title: 'four', word: 'working' }] });
      expect(document.querySelector('.ls-cross')).toBeNull();

      await fireEvent.click(screen.getByRole('radio', { name: 'Four' }));
      const cross = document.querySelector<HTMLElement>('.ls-cross')!;
      expect([...panesOf().children].slice(0, 4).every((one) => one.classList.contains('pane'))).toBe(true);

      await fireEvent.dblClick(cross);
      expect(onsplit).toHaveBeenLastCalledWith(4, [0.5, 0.5]);
    });

    it('lets Ctrl+Alt with the arrows still move between the panes from a divider', async () => {
      const onpick = vi.fn();
      deck({ layout: 2, onpick });

      await fireEvent.keyDown(width()!, { key: 'ArrowRight', ctrlKey: true, altKey: true });

      expect(onpick).toHaveBeenCalledWith('b');
    });
  });
});

describe('RunBoard', () => {
  it('puts the run that needs you first, with its question answerable in the row', () => {
    render(RunBoard, {
      runs: [
        { id: 'r1', goal: 'Tidy the docs', team: 'docs-crew', word: 'running', spent: 0.4, cap: 2 },
        { id: 'r2', goal: 'Fix the upload', team: 'bug-hunt', word: 'waiting for you', spent: 1.1, cap: 2, gate: { kind: 'confirm', gate: 'g1', question: 'Merge it?' } },
      ],
    });

    const rows = [...document.querySelectorAll('.brow .t')].map((node) => node.textContent);
    expect(rows).toEqual(['Fix the upload', 'Tidy the docs']);
    // The same command as the run's own page shows for the same answer.
    expect(screen.getByText('$ loadout team gate r2 --gate g1 --answer yes --by dashboard')).toBeTruthy();
  });

  it('says when a run is projected over its cap', () => {
    render(RunBoard, { runs: [{ id: 'r1', goal: 'Big job', team: 't', word: 'running', spent: 1.5, cap: 2, projected: 2.6 }] });

    expect(screen.getByText('projected over')).toBeTruthy();
  });
});

describe('QuietList', () => {
  it('lists what needs you first with its question, and the rest after', async () => {
    const onopen = vi.fn();
    render(QuietList, {
      items: [
        { id: 's1', kind: 'session', title: 'fix-tests', word: 'working', where: 'starstats' },
        { id: 'r1', kind: 'team run', title: 'Fix the upload', word: 'waiting for you', where: 'bug-hunt', ask: 'Merge it?' },
      ],
      onopen,
    });

    const lines = [...document.querySelectorAll('.ls-quiet li')].map((node) => node.textContent?.trim().slice(0, 14));
    expect(lines[0]).toBe('Needs you');
    expect(lines[1]).toContain('Fix the upload');
    expect(screen.getByText('Merge it?')).toBeTruthy();

    await fireEvent.click(screen.getByRole('button', { name: /fix-tests/ }));
    expect(onopen.mock.calls[0][0].id).toBe('s1');
  });
});

describe('FocusFrame', () => {
  it('gives the keyboard back to the line it was opened from on Escape', async () => {
    document.body.innerHTML = '<button id="line">fix-tests</button>';
    const line = document.getElementById('line') as HTMLButtonElement;
    line.focus();
    const onclose = vi.fn();

    render(FocusFrame, { title: 'fix-tests', onclose });
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(document.activeElement?.textContent).toContain('Back');

    await fireEvent.keyDown(document.activeElement!, { key: 'Escape' });

    expect(onclose).toHaveBeenCalledOnce();
    expect(document.activeElement).toBe(line);
  });

  it('simply appears under no motion', () => {
    document.documentElement.setAttribute('data-motion', 'none');
    render(FocusFrame, { title: 'quiet', from: new DOMRect(10, 10, 100, 20) });

    const frame = screen.getByRole('region', { name: 'quiet' });
    expect(frame.classList.contains('from')).toBe(false);
    expect(frame.classList.contains('fade')).toBe(false);
    document.documentElement.removeAttribute('data-motion');
  });
});
