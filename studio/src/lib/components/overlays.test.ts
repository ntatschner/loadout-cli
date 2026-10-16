// Dialog and CommandPalette: focus moves in, is held, and comes back; Escape
// closes; the palette finds and runs by keyboard.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { tick } from 'svelte';
import Dialog from './Dialog.svelte';
import CommandPalette from './CommandPalette.svelte';
import { paletteRows } from '../palette';

describe('Dialog', () => {
  afterEach(() => document.querySelectorAll('body > button').forEach((one) => one.remove()));

  it('is a modal dialog named by its title, with focus inside', async () => {
    const opener = document.createElement('button');
    document.body.appendChild(opener);
    opener.focus();

    render(Dialog, { title: 'New session', actions: [{ label: 'Launch', cmd: 'runs the command above' }] });
    await tick();
    await tick();

    const dialog = screen.getByRole('dialog', { name: 'New session' });
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(dialog.contains(document.activeElement)).toBe(true);
  });

  it('holds Tab inside, both ways', async () => {
    render(Dialog, { title: 'Hold', actions: [{ label: 'First' }] });
    await tick();
    await tick();

    const buttons = within(screen.getByRole('dialog')).getAllByRole('button');
    const first = buttons[0];
    const last = buttons[buttons.length - 1];

    last.focus();
    await fireEvent.keyDown(last, { key: 'Tab' });
    expect(document.activeElement).toBe(first);

    await fireEvent.keyDown(first, { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(last);
  });

  it('closes on Escape and on the dim', async () => {
    const onclose = vi.fn();
    render(Dialog, { title: 'Close me', onclose });

    await fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' });
    expect(onclose).toHaveBeenCalledTimes(1);

    // On the body, with the panel: a dialog is never inside the page it covers.
    await fireEvent.click(document.querySelector('.ls-scrim')!);
    expect(onclose).toHaveBeenCalledTimes(2);
  });

  it('closes after an action unless the action keeps it open', async () => {
    const onclose = vi.fn();
    render(Dialog, { title: 'Act', onclose, actions: [{ label: 'Keep', onpress: () => true }, { label: 'Go', onpress: () => undefined }] });

    await fireEvent.click(screen.getByRole('button', { name: /Keep/ }));
    await tick();
    expect(onclose).not.toHaveBeenCalled();

    await fireEvent.click(screen.getByRole('button', { name: /Go/ }));
    await tick();
    expect(onclose).toHaveBeenCalledTimes(1);
  });

  it('is an alertdialog when it is about something that cannot be undone, with focus on the safe answer', async () => {
    render(Dialog, { title: 'Sign every browser out?', danger: true, actions: [{ label: 'Sign out', kind: 'danger' }] });
    await tick();
    await tick();

    expect(screen.getByRole('alertdialog')).toBeTruthy();
    expect(document.activeElement?.textContent).toContain('Cancel');
  });

  it('sits outside the page it makes inert, and gives it back', async () => {
    const page = document.createElement('main');
    page.id = 'main';
    document.body.appendChild(page);

    const shown = render(Dialog, { props: { title: 'Inert', actions: [{ label: 'Go' }] }, target: page });
    await tick();

    expect(page.inert).toBe(true);
    expect(page.contains(screen.getByRole('dialog'))).toBe(false);

    shown.unmount();
    expect(page.inert).toBe(false);
    page.remove();
  });

  it('gives focus back to what opened it', async () => {
    const opener = document.createElement('button');
    document.body.appendChild(opener);
    opener.focus();

    const shown = render(Dialog, { title: 'Back' });
    await tick();
    shown.unmount();

    expect(document.activeElement).toBe(opener);
  });
});

describe('CommandPalette', () => {
  const ran: string[] = [];
  const groups = [
    { name: 'Go to', items: [{ key: 'home', label: 'Home', sub: 'page', run: () => ran.push('home') }, { key: 'sessions', label: 'Sessions', sub: 'page', run: () => ran.push('sessions') }] },
    { name: 'In flight', items: [{ key: 'r', label: 'Release 0.66.0', sub: 'release-crew · running', run: () => ran.push('release') }] },
    { name: 'Do', items: [{ key: 'a', label: 'Enter Ambient', sub: 'changes nothing · A', run: () => ran.push('ambient') }] },
  ];

  it('is a combobox controlling a listbox, grouped', async () => {
    render(CommandPalette, { groups });
    await tick();
    await tick();

    const box = screen.getByRole('combobox');
    expect(document.activeElement).toBe(box);
    expect(box.getAttribute('aria-controls')).toBe(screen.getByRole('listbox').id);
    expect(screen.getAllByRole('group').map((g) => g.textContent)).toHaveLength(3);
  });

  it('filters as you type and runs the active row on Enter', async () => {
    ran.length = 0;
    const onclose = vi.fn();
    render(CommandPalette, { groups, onclose });
    await tick();

    const box = screen.getByRole('combobox');
    await fireEvent.input(box, { target: { value: 'sess' } });
    await tick();

    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual([expect.stringContaining('Sessions')]);

    await fireEvent.keyDown(box, { key: 'Enter' });

    expect(ran).toEqual(['sessions']);
    expect(onclose).toHaveBeenCalled();
  });

  it('moves with the arrow keys through every group, wrapping', async () => {
    render(CommandPalette, { groups });
    await tick();

    const box = screen.getByRole('combobox');
    const options = screen.getAllByRole('option');
    expect(box.getAttribute('aria-activedescendant')).toBe(options[0].id);

    await fireEvent.keyDown(box, { key: 'ArrowUp' });
    expect(box.getAttribute('aria-activedescendant')).toBe(options[3].id);

    await fireEvent.keyDown(box, { key: 'ArrowDown' });
    expect(box.getAttribute('aria-activedescendant')).toBe(options[0].id);
  });

  it('says so when nothing matches', async () => {
    render(CommandPalette, { groups });
    await tick();

    await fireEvent.input(screen.getByRole('combobox'), { target: { value: 'zzz' } });

    expect(screen.getByText(/Nothing matches/)).toBeTruthy();
  });

  it('ranks a match on the label above a match only in its facts, and moves that group up', () => {
    const shown = paletteRows(
      [
        { name: 'Go to', items: [{ key: 'h', label: 'Home', sub: 'running' }] },
        { name: 'In flight', items: [{ key: 'r', label: 'running tests', sub: 'session' }] },
      ],
      'running',
    );

    expect(shown.map((group) => group.name)).toEqual(['In flight', 'Go to']);
  });
});
