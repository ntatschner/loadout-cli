// Terminal Focus's tabs managed as a person would: closing an ended tab
// clears it at once, closing a running one asks Hide or End and End asks
// again, F2 renames in place, the tab's menu by keyboard, reordering by
// Ctrl+Shift+arrows and by dragging, hidden tabs brought back, and a Studio
// that is only watching never offering to rename or end.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';
import SessionTabs from './SessionTabs.svelte';
import type { DeckSession } from '../modes';

const sessions: DeckSession[] = [
  { id: 'a', title: 'fix-tests', word: 'working', where: 'starstats', project: 'starstats', kind: 'launch', launchTitle: 'starstats (claude)', namesAgent: true },
  { id: 'b', title: 'docs', word: 'working', where: 'storefront', project: 'storefront', kind: 'launch', launchTitle: 'docs', namesAgent: false },
  { id: 'c', title: 'release', word: 'ended', ended: true, kind: 'launch', launchTitle: 'release' },
];

function tabs(props: Record<string, unknown> = {}) {
  const calls = {
    onpick: vi.fn(),
    onmove: vi.fn(),
    onrename: vi.fn(),
    onhide: vi.fn(),
    onunhide: vi.fn(),
    onclear: vi.fn(),
    onend: vi.fn(),
  };

  render(SessionTabs, { sessions, current: 'a', ...calls, ...props });

  return calls;
}

const tab = (name: RegExp) => screen.getByRole('tab', { name });
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

afterEach(() => {
  document.body.innerHTML = '';
});

describe('SessionTabs', () => {
  it('clears an ended tab at once when it is closed, asking nothing', async () => {
    const calls = tabs();

    await fireEvent.click(screen.getByRole('button', { name: 'Close release' }));

    expect(calls.onclear).toHaveBeenCalledWith('c');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(calls.onend).not.toHaveBeenCalled();
  });

  it('asks Hide or End for a running tab, and Hide only hides it', async () => {
    const calls = tabs();

    await fireEvent.click(screen.getByRole('button', { name: 'Close docs' }));
    const asked = screen.getByRole('dialog', { name: 'Close docs?' });
    expect(asked.textContent).toContain('Sessions page');

    await fireEvent.click(screen.getByRole('button', { name: /^Hide/ }));
    await tick();

    expect(calls.onhide).toHaveBeenCalledWith('b');
    expect(calls.onend).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('asks a second time before ending, naming the session and its project, and ends nothing on Cancel', async () => {
    const calls = tabs();

    await fireEvent.keyDown(tab(/docs/), { key: 'Delete' });
    await fireEvent.click(screen.getByRole('button', { name: /End session…/ }));
    await tick();

    const confirm = screen.getByRole('alertdialog', { name: 'End docs?' });
    expect(confirm.textContent).toContain('in storefront');
    expect(confirm.textContent).toContain('$ loadout session end b --yes');
    expect(calls.onend).not.toHaveBeenCalled();

    await fireEvent.click(screen.getByRole('button', { name: /^Cancel/ }));
    await tick();
    expect(calls.onend).not.toHaveBeenCalled();

    await fireEvent.keyDown(tab(/docs/), { key: 'Delete' });
    await fireEvent.click(screen.getByRole('button', { name: /End session…/ }));
    await tick();
    await fireEvent.click(screen.getByRole('button', { name: /^End session$/ }));

    expect(calls.onend).toHaveBeenCalledWith('b');
  });

  it('renames in place on F2: Enter saves, Escape cancels, and empty goes back to the launch name', async () => {
    const calls = tabs();

    await fireEvent.keyDown(tab(/fix-tests/), { key: 'F2' });
    let field = screen.getByRole('textbox', { name: 'New name for fix-tests' }) as HTMLInputElement;
    expect(field.value).toBe('fix-tests');
    expect(document.getElementById(field.getAttribute('aria-describedby')!)!.textContent).toContain('Its agent is given the name too');

    await fireEvent.input(field, { target: { value: '  release notes ' } });
    await fireEvent.keyDown(field, { key: 'Enter' });
    expect(calls.onrename).toHaveBeenLastCalledWith('a', 'release notes');

    await fireEvent.keyDown(tab(/fix-tests/), { key: 'F2' });
    field = screen.getByRole('textbox') as HTMLInputElement;
    await fireEvent.input(field, { target: { value: 'never mind' } });
    await fireEvent.keyDown(field, { key: 'Escape' });
    expect(calls.onrename).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('textbox')).toBeNull();

    await fireEvent.keyDown(tab(/fix-tests/), { key: 'F2' });
    field = screen.getByRole('textbox') as HTMLInputElement;
    await fireEvent.input(field, { target: { value: '' } });
    await fireEvent.keyDown(field, { key: 'Enter' });
    expect(calls.onrename).toHaveBeenLastCalledWith('a', null);
  });

  it('says when only Loadout will show a new name', async () => {
    tabs();

    await fireEvent.keyDown(tab(/docs/), { key: 'F2' });
    const field = screen.getByRole('textbox');

    expect(document.getElementById(field.getAttribute('aria-describedby')!)!.textContent).toContain('Only Loadout shows this name');
  });

  it('opens the tab menu from the keyboard and moves through it, back to the tab on Escape', async () => {
    const calls = tabs();

    await fireEvent.keyDown(tab(/docs/), { key: 'ContextMenu' });
    await settle();

    const menu = screen.getByRole('menu', { name: 'Tab docs' });
    const items = [...menu.querySelectorAll('[role="menuitem"]')].map((item) => item.textContent?.replace(/\s+/g, ' ').trim());
    expect(items).toEqual(['RenameF2', 'Move leftCtrl Shift ←', 'Move rightCtrl Shift →', 'Hide', 'End session…']);
    expect(document.activeElement?.textContent).toContain('Rename');

    await fireEvent.keyDown(menu, { key: 'ArrowUp' });
    expect(document.activeElement?.textContent).toContain('End session');

    await fireEvent.keyDown(menu, { key: 'Escape' });
    await settle();
    expect(screen.queryByRole('menu')).toBeNull();
    expect(document.activeElement).toBe(tab(/docs/));

    await fireEvent.keyDown(tab(/docs/), { key: 'F10', shiftKey: true });
    await settle();
    await fireEvent.click(screen.getByRole('menuitem', { name: /Hide/ }));
    expect(calls.onhide).toHaveBeenCalledWith('b');
  });

  it('offers the launch name back on a renamed tab', async () => {
    const calls = tabs();

    await fireEvent.keyDown(tab(/fix-tests/), { key: 'ContextMenu' });
    await settle();
    await fireEvent.click(screen.getByRole('menuitem', { name: /Back to “starstats \(claude\)”/ }));

    expect(calls.onrename).toHaveBeenCalledWith('a', null);
  });

  it('only watching, leaves Rename and End out and hides a running tab on close without asking', async () => {
    const calls = tabs({ changes: false });

    await fireEvent.keyDown(tab(/docs/), { key: 'F2' });
    expect(screen.queryByRole('textbox')).toBeNull();

    await fireEvent.keyDown(tab(/docs/), { key: 'ContextMenu' });
    await settle();
    const items = [...screen.getByRole('menu').querySelectorAll('[role="menuitem"]')].map((item) => item.textContent ?? '');
    expect(items.some((item) => /Rename|End session/.test(item))).toBe(false);
    expect(items.some((item) => item.startsWith('Hide'))).toBe(true);

    await fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });
    await fireEvent.click(screen.getByRole('button', { name: 'Close docs' }));

    expect(calls.onhide).toHaveBeenCalledWith('b');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(calls.onrename).not.toHaveBeenCalled();
  });

  it('moves a tab along the strip on Ctrl+Shift+arrows, keeping the keyboard on it', async () => {
    const calls = tabs();

    await fireEvent.keyDown(tab(/fix-tests/), { key: 'ArrowRight', ctrlKey: true, shiftKey: true });
    expect(calls.onmove).toHaveBeenCalledWith(['b', 'a', 'c']);
    expect(calls.onpick).not.toHaveBeenCalled();

    await fireEvent.keyDown(tab(/fix-tests/), { key: 'ArrowLeft', ctrlKey: true, shiftKey: true });
    expect(calls.onmove).toHaveBeenCalledTimes(1);
  });

  it('moves a tab where it is dropped, and a drag is not a click', async () => {
    const calls = tabs();

    // Laid out 100 pixels apart, as jsdom lays out nothing.
    sessions.forEach((one, index) => {
      tab(new RegExp(one.title)).getBoundingClientRect = () => ({ left: index * 100, width: 100, top: 0, bottom: 40, right: index * 100 + 100, height: 40, x: index * 100, y: 0, toJSON: () => ({}) });
    });

    const strip = screen.getByRole('tablist');
    await fireEvent.pointerDown(tab(/release/), { button: 0, clientX: 250, pointerId: 1 });
    await fireEvent.pointerMove(strip, { clientX: 120, pointerId: 1 });
    expect(document.querySelector('.tab.before')?.getAttribute('data-id')).toBe('b');

    await fireEvent.pointerMove(strip, { clientX: 20, pointerId: 1 });
    expect(document.querySelector('.tab.before')?.getAttribute('data-id')).toBe('a');

    await fireEvent.pointerUp(strip, { pointerId: 1 });
    await fireEvent.click(tab(/release/));

    expect(calls.onmove).toHaveBeenCalledWith(['c', 'a', 'b']);
    expect(calls.onpick).not.toHaveBeenCalled();
    expect(document.querySelector('.tab.before')).toBeNull();
  });

  it('a press that does not move is still a click', async () => {
    const calls = tabs();

    await fireEvent.pointerDown(tab(/docs/), { button: 0, clientX: 150, pointerId: 1 });
    await fireEvent.pointerMove(screen.getByRole('tablist'), { clientX: 152, pointerId: 1 });
    await fireEvent.pointerUp(screen.getByRole('tablist'), { pointerId: 1 });
    await fireEvent.click(tab(/docs/));

    expect(calls.onmove).not.toHaveBeenCalled();
    expect(calls.onpick).toHaveBeenCalledWith('b');
  });

  it('brings a hidden tab back from the count at the end of the strip', async () => {
    const calls = tabs({ sessions: sessions.slice(0, 1), hidden: [sessions[1]] });

    await fireEvent.click(screen.getByRole('button', { name: '1 hidden' }));
    await settle();
    await fireEvent.click(screen.getByRole('menuitem', { name: 'Show docs' }));

    expect(calls.onunhide).toHaveBeenCalledWith('b');
    expect(calls.onpick).toHaveBeenCalledWith('b');
  });
});
