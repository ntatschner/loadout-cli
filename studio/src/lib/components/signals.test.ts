// Toasts, Meter, Urgency and SideRail: what each says, and that colour is
// never the only thing saying it.
import { describe, expect, it, vi } from 'vitest';
import { createRawSnippet, tick, type Component, type Snippet } from 'svelte';
import { fireEvent, render, screen } from '@testing-library/svelte';
import Toasts from './Toasts.svelte';
import Meter from './Meter.svelte';
import Urgency from './Urgency.svelte';
import SideRail from './SideRail.svelte';
import { FOLD_AFTER, ToastStack } from '../toasts.svelte';
import { applyUrgency } from '../urgency';

describe('Toasts', () => {
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

  it('folds a Done after the fold time, and leaves a question open until it is answered', async () => {
    const stack = new ToastStack();
    render(Toasts, { stack, clock: false });

    stack.add({ level: 'done', text: 'Saved' });
    stack.add({ level: 'needs', text: 'release-crew asks: push the tag?', actions: [{ label: 'Approve', cmd: '$ loadout team gate r1 --answer Approve --by dashboard' }] });

    for (let second = 0; second < FOLD_AFTER; second++) {
      stack.tick();
    }

    await tick();
    expect(stack.list.find((one) => one.level === 'done')?.folded).toBe(true);
    expect(stack.list.find((one) => one.level === 'needs')?.folded).toBe(false);

    await fireEvent.click(screen.getByRole('button', { name: /Approve/ }));
    await tick();

    expect(screen.getByRole('status').textContent).toBe('Approve');
    expect(stack.list.find((one) => one.text === 'Approve')?.settled).not.toBeNull();
  });

  it('folds nothing while the pointer or keyboard is on the stack', async () => {
    const stack = new ToastStack();
    const { container } = render(Toasts, { stack, clock: false });

    stack.add({ level: 'info', text: 'writer/1 reported back' });
    await tick();
    await fireEvent.mouseEnter(container.querySelector('.ls-toasts')!);

    for (let second = 0; second < FOLD_AFTER * 2; second++) {
      stack.tick();
    }

    expect(stack.list[0].folded).toBe(false);
  });

  it('opens a folded chip again', async () => {
    const stack = new ToastStack();
    render(Toasts, { stack, clock: false });

    stack.add({ level: 'info', text: 'round 3 started' });

    for (let second = 0; second < FOLD_AFTER; second++) {
      stack.tick();
    }

    await tick();
    await fireEvent.click(screen.getByRole('button', { name: /round 3 started\. Open it/ }));

    expect(stack.list[0].folded).toBe(false);
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
