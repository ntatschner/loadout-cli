// Toasts, Meter, Urgency and SideRail: what each says, and that colour is
// never the only thing saying it.
import { describe, expect, it, vi } from 'vitest';
import { createRawSnippet, tick, type Component, type Snippet } from 'svelte';
import { fireEvent, render, screen } from '@testing-library/svelte';
import Toasts from './Toasts.svelte';
import Meter from './Meter.svelte';
import Urgency from './Urgency.svelte';
import SideRail from './SideRail.svelte';
import { GONE_AFTER, OPEN_FOR, ToastStack } from '../toasts.svelte';
import { applyUrgency } from '../urgency';

describe('Toasts', () => {
  function seconds(stack: ToastStack, count: number) {
    for (let second = 0; second < count; second++) {
      stack.tick();
    }
  }

  it('says each level in words, and reads a Problem out at once', async () => {
    const stack = new ToastStack();
    render(Toasts, { stack, clock: false });

    stack.add({ level: 'done', text: 'Started in starstats', cmd: '$ loadout launch starstats' });
    stack.add({ level: 'problem', text: 'round 3 failed' });
    await tick();

    expect(screen.getByText('Done')).toBeTruthy();
    expect(screen.getByText('Problem')).toBeTruthy();
    expect(screen.getByText('$ loadout launch starstats')).toBeTruthy();
    expect(screen.getByRole('alert').textContent).toBe('Problem: round 3 failed');
    expect(screen.getByRole('status').textContent).toBe('Done: Started in starstats');
  });

  it('folds a Done and a notice at the open time and lets them go into the history at the gone time', () => {
    const stack = new ToastStack();

    stack.add({ level: 'done', text: 'Saved' });
    stack.add({ level: 'info', text: 'writer/1 reported back' });

    seconds(stack, OPEN_FOR - 1);
    expect(stack.list.every((one) => !one.folded)).toBe(true);

    stack.tick();
    expect(stack.list.every((one) => one.folded)).toBe(true);

    seconds(stack, GONE_AFTER - OPEN_FOR - 1);
    expect(stack.list).toHaveLength(2);

    stack.tick();
    expect(stack.list).toHaveLength(0);
    expect(stack.history.map((one) => one.text)).toEqual(['writer/1 reported back', 'Saved']);
  });

  it('leaves a question open until it is answered, then follows Done from the answer', async () => {
    const stack = new ToastStack();
    render(Toasts, { stack, clock: false });

    stack.add({ level: 'needs', text: 'release-crew asks: push the tag?', actions: [{ label: 'Approve', cmd: '$ loadout team gate r1 --answer Approve --by dashboard' }] });
    seconds(stack, GONE_AFTER * 6);
    await tick();

    expect(stack.list[0].folded).toBe(false);

    await fireEvent.click(screen.getByRole('button', { name: /Approve/ }));
    await tick();

    expect(screen.getByRole('status').textContent).toBe('Approve');
    expect(stack.list[0].settled).toBe(true);

    seconds(stack, OPEN_FOR);
    expect(stack.list[0].folded).toBe(true);

    seconds(stack, GONE_AFTER - OPEN_FOR);
    expect(stack.list).toHaveLength(0);
    expect(stack.history[0]).toMatchObject({ level: 'done', text: 'Approve' });
  });

  it('stops only the toast under the pointer, and its neighbours go on', async () => {
    const stack = new ToastStack();
    const { container } = render(Toasts, { stack, clock: false });

    stack.add({ level: 'info', text: 'older' });
    stack.add({ level: 'info', text: 'newer' });
    await tick();

    const cards = Array.from(container.querySelectorAll('.ls-toast'));
    const older = cards.find((card) => card.textContent?.includes('older'))!;
    await fireEvent.mouseEnter(older);
    seconds(stack, GONE_AFTER);

    expect(stack.list.map((one) => one.text)).toEqual(['older']);
    expect(stack.list[0].folded).toBe(false);

    await fireEvent.mouseLeave(older);
    seconds(stack, GONE_AFTER);
    expect(stack.list).toHaveLength(0);
  });

  it('stops the toast holding the keyboard', async () => {
    const stack = new ToastStack();
    render(Toasts, { stack, clock: false });

    stack.add({ level: 'done', text: 'Saved' });
    stack.add({ level: 'done', text: 'Pushed' });
    await tick();

    await fireEvent.focusIn(screen.getByRole('button', { name: 'Dismiss: Saved' }));
    seconds(stack, GONE_AFTER);

    expect(stack.list.map((one) => one.text)).toEqual(['Saved']);
  });

  it('puts a dismissed toast into the history too', async () => {
    const stack = new ToastStack();
    render(Toasts, { stack, clock: false });

    stack.add({ level: 'problem', text: 'round 3 failed' });
    await tick();
    await fireEvent.click(screen.getByRole('button', { name: 'Dismiss: round 3 failed' }));

    expect(stack.list).toHaveLength(0);
    expect(stack.history[0]).toMatchObject({ level: 'problem', text: 'round 3 failed' });
    expect(screen.getByRole('button', { name: /Earlier/ }).textContent).toContain('1');
  });

  it('opens a folded chip again, for another run of the times', async () => {
    const stack = new ToastStack();
    render(Toasts, { stack, clock: false });

    stack.add({ level: 'info', text: 'round 3 started' });
    seconds(stack, OPEN_FOR + 2);

    await tick();
    await fireEvent.click(screen.getByRole('button', { name: /round 3 started\. Open it/ }));

    expect(stack.list[0].folded).toBe(false);

    seconds(stack, GONE_AFTER - 1);
    expect(stack.list).toHaveLength(1);
  });

  it('counts a repeated Done once, with how many times', async () => {
    const stack = new ToastStack();
    render(Toasts, { stack, clock: false });

    stack.add({ level: 'done', text: 'Session ended' });
    stack.add({ level: 'done', text: 'Session ended' });
    stack.add({ level: 'done', text: 'Session ended' });
    await tick();

    expect(stack.list).toHaveLength(1);
    expect(stack.list[0].count).toBe(3);
    expect(screen.getByText('×3')).toBeTruthy();

    seconds(stack, GONE_AFTER);
    expect(stack.history).toHaveLength(1);
    expect(stack.history[0].count).toBe(3);
  });

  it('shows everything as a chip in a named place, and opens a waiting question again on leaving it', async () => {
    const stack = new ToastStack();
    const { container, rerender } = render(Toasts, { stack, clock: false, place: null });

    stack.add({ level: 'needs', text: 'writer/2 asks: delete the branch?', actions: [{ label: 'Yes', cmd: '' }] });
    stack.add({ level: 'done', text: 'Saved' });
    await tick();

    await rerender({ stack, clock: false, place: 'pure' });
    expect(stack.list.every((one) => one.folded)).toBe(true);
    expect(container.querySelectorAll('.ls-toast')).toHaveLength(0);

    stack.add({ level: 'info', text: 'arrived here' });
    expect(stack.list[0].folded).toBe(true);

    await rerender({ stack, clock: false, place: null });
    const needs = stack.list.find((one) => one.level === 'needs')!;
    expect(needs.folded).toBe(false);
    expect(stack.list.filter((one) => one.level !== 'needs').every((one) => one.folded)).toBe(true);
    // Nothing moved: the order is the order they came in.
    expect(stack.list.map((one) => one.text)).toEqual(['arrived here', 'Saved', 'writer/2 asks: delete the branch?']);
  });

  it('lets Done and notice chips give way in a named place, and never a waiting question', async () => {
    const stack = new ToastStack();
    const { container } = render(Toasts, { stack, clock: false, place: 'session' });

    stack.add({ level: 'done', text: 'Saved' });
    stack.add({ level: 'problem', text: 'round 3 failed' });
    await tick();

    const chips = Array.from(container.querySelectorAll('.ls-toast-chip'));
    expect(chips.find((chip) => chip.textContent?.includes('Saved'))?.hasAttribute('data-gives-way')).toBe(true);
    expect(chips.find((chip) => chip.textContent?.includes('round 3'))?.hasAttribute('data-gives-way')).toBe(false);
  });

  it('gives way only while the pointer is near', async () => {
    const stack = new ToastStack();
    const { container } = render(Toasts, { stack, clock: false, place: 'desk' });

    stack.add({ level: 'done', text: 'Saved' });
    await tick();

    const chip = container.querySelector<HTMLElement>('[data-gives-way]')!;
    chip.getBoundingClientRect = () => ({ left: 900, right: 1000, top: 100, bottom: 130, width: 100, height: 30, x: 900, y: 100, toJSON: () => ({}) });

    window.dispatchEvent(new MouseEvent('pointermove', { clientX: 880, clientY: 110 }));
    await tick();
    expect(container.querySelector('.ls-toasts')?.classList.contains('near')).toBe(true);

    window.dispatchEvent(new MouseEvent('pointermove', { clientX: 400, clientY: 500 }));
    await tick();
    expect(container.querySelector('.ls-toasts')?.classList.contains('near')).toBe(false);
  });

  it('folds with Ctrl+Shift+X, and clears all but a waiting question when pressed again', async () => {
    const stack = new ToastStack();
    render(Toasts, { stack, clock: false });

    stack.add({ level: 'needs', text: 'writer/2 asks: delete the branch?' });
    stack.add({ level: 'done', text: 'Saved' });
    await tick();

    await fireEvent.keyDown(window, { key: 'X', code: 'KeyX', ctrlKey: true, shiftKey: true });
    expect(stack.list.every((one) => one.folded)).toBe(true);

    await fireEvent.keyDown(window, { key: 'X', code: 'KeyX', ctrlKey: true, shiftKey: true });
    expect(stack.list.map((one) => one.level)).toEqual(['needs']);
    expect(stack.history.map((one) => one.text)).toEqual(['Saved']);
  });
});

describe('Meter', () => {
  it('is a meter that says in words when it is past the threshold', () => {
    render(Meter, { label: 'Tokens today', value: 9700, max: 12000, threshold: 9600, unit: 'tokens', format: (v: number) => v.toLocaleString('en-GB') });

    const meter = screen.getByRole('meter', { name: 'Tokens today' });
    expect(meter.getAttribute('aria-valuenow')).toBe('9700');
    expect(meter.getAttribute('aria-valuetext')).toBe('9,700 tokens, past your threshold');
    expect(screen.getByText('past your threshold')).toBeTruthy();
  });

  it('says nothing about a threshold it has not reached', () => {
    render(Meter, { label: 'Tokens today', value: 100, max: 12000, threshold: 9600 });

    expect(screen.getByRole('meter').getAttribute('aria-valuetext')).toBe('100');
    expect(screen.queryByText('past your threshold')).toBeNull();
  });
});

describe('Urgency', () => {
  // A generic component, typed here for the rows the tests give it.
  type Item = { key: string; title: string; waiting?: boolean; problem?: boolean; askedAt?: number };
  const UrgencyOf = Urgency as unknown as Component<{ items: Item[]; rising?: boolean; row: Snippet<[Item, number]> }>;
  const row = createRawSnippet((item: () => Item) => ({ render: () => `<span>${item().title}</span>` })) as Snippet<[Item, number]>;

  it('puts a problem before a question and an older question before a newer one, and raises the first', () => {
    const { container } = render(UrgencyOf, {
      items: [
        { key: 'new', title: 'newer question', waiting: true, askedAt: 20 },
        { key: 'calm', title: 'running quietly' },
        { key: 'old', title: 'older question', waiting: true, askedAt: 5 },
        { key: 'bad', title: 'round failed', problem: true },
      ],
      row,
    });

    const rows = Array.from(container.querySelectorAll('li'));
    expect(rows.map((one) => one.textContent)).toEqual(['round failed', 'older question', 'newer question']);
    expect(rows[0].className).toBe('ls-rise problem');
    expect(rows[1].className).toBe('ls-recede');
  });

  it('raises nothing while rising is off', () => {
    const { container } = render(UrgencyOf, { items: [{ key: 'q', title: 'a question', waiting: true }], rising: false, row });

    expect(container.querySelector('li')?.className).toBe('');
  });

  it('applyUrgency raises one row and recedes the rest, and -1 clears them', () => {
    const rows = [document.createElement('li'), document.createElement('li')];

    applyUrgency(rows, 1, 'needs');
    expect(rows[1].classList.contains('ls-rise')).toBe(true);
    expect(rows[0].classList.contains('ls-recede')).toBe(true);

    applyUrgency(rows, -1);
    expect(rows.some((one) => one.classList.contains('ls-rise') || one.classList.contains('ls-recede'))).toBe(false);
  });
});

describe('SideRail', () => {
  const pages = [
    { key: 'home', name: 'Home', fact: '1 waiting on you', need: true },
    { key: 'sessions', name: 'Sessions', fact: '2 running' },
  ];

  it('marks the current page and says each count in words', () => {
    render(SideRail, { pages, current: 'sessions' });

    const nav = screen.getAllByRole('navigation', { name: 'Studio' })[0];
    const sessions = Array.from(nav.querySelectorAll('a')).find((a) => a.textContent?.includes('Sessions'))!;
    expect(sessions.getAttribute('aria-current')).toBe('page');
    expect(nav.textContent).toContain('1 waiting on you');
    expect(nav.querySelector('a.need')?.textContent).toContain('Home');
  });

  it('opens Search from the rail', async () => {
    const onsearch = vi.fn();
    render(SideRail, { pages, current: 'home', onsearch });

    await fireEvent.click(screen.getByRole('button', { name: /Search/ }));

    expect(onsearch).toHaveBeenCalled();
  });

  it('slides the pages in from the Pages tab, and Escape puts focus back on the tab', async () => {
    render(SideRail, { pages, current: 'home' });

    const tab = screen.getByRole('button', { name: 'Pages' });
    await fireEvent.click(tab);
    await tick();

    expect(tab.getAttribute('aria-expanded')).toBe('true');
    expect(screen.getAllByRole('navigation')).toHaveLength(2);

    await fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
    await tick();

    expect(tab.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(tab);
  });
});
