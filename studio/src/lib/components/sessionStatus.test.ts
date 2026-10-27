// A hosted session's status as Studio draws it: the mark on a tab and a
// Pure row, the strip on the terminal, and the block in Details. Views are
// found by role and name, never by position.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import { createRawSnippet, tick } from 'svelte';
import ContextMark from './ContextMark.svelte';
import SessionTabs from './SessionTabs.svelte';
import StatusBlock from './StatusBlock.svelte';
import StatusStrip from './StatusStrip.svelte';
import WorkDesk from './WorkDesk.svelte';
import { pureGroups } from '../pure';
import { snapshot } from '../statusFixture';
import type { Work } from '../lines';
import type { DeckSession } from '../modes';

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;

const at = Date.parse('2026-10-27T12:00:00Z');

afterEach(() => {
  document.body.innerHTML = '';
});

describe('the context mark', () => {
  it.each([
    [42, 'ok', 'Context 42% used'],
    [70, 'warn', 'Context 70% used, filling'],
    [93, 'urgent', 'Context 93% used, nearly full'],
  ])('at %i%% reads as %s, in words as well as colour', (percent, level, words) => {
    render(ContextMark, { percent });

    const mark = screen.getByRole('img', { name: words });
    expect(mark.classList.contains(level)).toBe(true);
  });

  it('draws nothing where the session has not said', () => {
    const { container } = render(ContextMark, { percent: null });

    expect(container.querySelector('.ls-ctx')).toBeNull();
  });
});

describe('the strip on the terminal', () => {
  it('shows the model, context, cost, the five-hour window and the specialists', () => {
    render(StatusStrip, { status: snapshot(), now: at + 5_000 });

    const strip = screen.getByRole('group', { name: 'Session status' });
    expect(strip.textContent).toContain('Opus 5.5');
    expect(strip.textContent).toContain('42%');
    expect(strip.textContent).toContain('$1.23');
    expect(strip.textContent).toContain('62%');
    expect(strip.textContent).toContain('2 specialists');
    expect(strip.textContent).toContain('just now');
    expect(strip.classList.contains('stale')).toBe(false);
  });

  it('leaves out what Claude did not send rather than drawing it as nought', () => {
    render(StatusStrip, { status: snapshot({ cost: null, limits: null, specialists: null }), now: at });

    const strip = screen.getByRole('group', { name: 'Session status' });
    expect(strip.textContent).not.toContain('cost');
    expect(strip.textContent).not.toContain('5h');
    expect(strip.textContent).not.toContain('specialist');
  });

  it('says when its figures are old', () => {
    render(StatusStrip, { status: snapshot(), now: at + 5 * 60_000 });

    const strip = screen.getByRole('group', { name: 'Session status' });
    expect(strip.classList.contains('stale')).toBe(true);
    expect(strip.textContent).toContain('old: 5m ago');
  });

  it('tells a screen reader only when the context crosses a threshold', async () => {
    const view = render(StatusStrip, { status: snapshot(), now: at });
    const said = () => screen.getByRole('status').textContent;

    expect(said()).toBe('');

    await view.rerender({ status: snapshot({ context: { ...snapshot().context!, usedPercent: 50 } }), now: at });
    await tick();
    // A figure moving within a level is not said.
    expect(said()).toBe('');

    await view.rerender({ status: snapshot({ context: { ...snapshot().context!, usedPercent: 91 } }), now: at });
    await tick();
    expect(said()).toBe('Context 91% used, nearly full');
  });
});

describe('the block in Details', () => {
  it('names each specialist with a link to it, and each window with its reset', () => {
    render(StatusBlock, { status: snapshot(), now: at });

    expect(screen.getByRole('link', { name: 'role.implementer' }).getAttribute('href')).toBe('#context/demo/specialist/role.implementer');
    expect(screen.getByRole('meter', { name: 'Context window used' }).getAttribute('aria-valuenow')).toBe('42');
    expect(screen.getByRole('meter', { name: 'Five-hour window' })).toBeTruthy();
    expect(document.body.textContent).toContain('resets in 2h10m');
    expect(document.body.textContent).toContain('$1.23');
  });

  it('acts only through links and a command shown whole, never a button that could type', () => {
    render(StatusBlock, { status: snapshot(), now: at });

    expect(screen.queryAllByRole('button')).toEqual([]);
    expect(document.querySelector('.ls-cmdline')?.textContent).toContain('loadout config set statusline-in-studio full');
  });

  it('says where there are no usage limits, and why', () => {
    render(StatusBlock, { status: snapshot({ limits: null }), now: at });

    expect(document.body.textContent).toContain('No usage limits');
  });
});

describe('the tabs and the Pure list', () => {
  const deck: DeckSession[] = [
    { id: 'a', title: 'fix-tests', word: 'working', kind: 'launch', context: 93 },
    { id: 'b', title: 'docs', word: 'working', kind: 'launch', context: null },
  ];

  it('mark each tab with its session’s context', () => {
    render(SessionTabs, { sessions: deck, current: 'a', onpick: vi.fn() });

    const first = screen.getByRole('tab', { name: /fix-tests/ });
    expect(first.querySelector('.ls-ctx.urgent')?.getAttribute('aria-label')).toBe('Context 93% used, nearly full');
    expect(screen.getByRole('tab', { name: /docs/ }).querySelector('.ls-ctx')).toBeNull();
  });

  it('carry the context into Pure, and none for a session that has ended', () => {
    const line = (over: Partial<Work>) => ({ kind: 'session', id: 'x', ref: 'x', title: 't', where: 'p', stage: 2, ...over }) as Work;
    const session = (percent: number, ended = false) => ({ id: 'x', ended, status: snapshot({ context: { ...snapshot().context!, usedPercent: percent } }) }) as unknown as Work['session'];

    const groups = pureGroups([line({ id: 'a', ref: 'a', session: session(75) }), line({ id: 'b', ref: 'b', stage: 4, session: session(80, true) })], [], 10);
    const items = groups.flatMap((group) => group.items);

    expect(items.find((item) => item.id === 'a')?.context).toBe(75);
    expect(items.find((item) => item.id === 'b')?.context).toBeNull();

    render(WorkDesk, {
      groups: [{ key: 'sessions', name: 'Sessions', items }],
      view: createRawSnippet(() => ({ render: () => '<div></div>' })),
      actions: () => [],
      commands: () => [],
    });

    const row = screen.getAllByRole('option').find((option) => option.dataset.id === 'a');
    expect(row?.querySelector('.ls-ctx.warn')?.getAttribute('aria-label')).toBe('Context 75% used, filling');
  });
});
