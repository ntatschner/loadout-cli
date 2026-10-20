// Terminal Focus's tabs as this browser keeps them: in the order put in,
// with hidden and cleared ones left off, ids the daemon no longer has let
// go, a session the address names brought back, and a name or an end sent
// to the daemon rather than kept here.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';
import type { Session } from '../lib/api';
import { TABS_KEY } from '../lib/tabs';

const renamed: [string, string | null][] = [];
let hosted: Session[] = [];
const stopped: string[] = [];

vi.mock('../lib/api', async (original) => {
  const real = await original<typeof import('../lib/api')>();

  return {
    ...real,
    sessions: {
      ...real.sessions,
      list: vi.fn(async () => hosted),
      rename: vi.fn(async (id: string, title: string | null) => {
        renamed.push([id, title]);

        return {} as Session;
      }),
      stop: vi.fn(async (id: string) => {
        stopped.push(id);

        return {} as Session;
      }),
    },
  };
});

// The live terminal is not what is under test, and opens a socket.
vi.mock('../lib/components/SessionPane.svelte', () => ({ default: () => undefined }));

const { studio } = await import('../lib/state.svelte');
const { default: TerminalFocus } = await import('./TerminalFocus.svelte');

function session(id: string, title: string, over: Partial<Session> = {}): Session {
  return {
    id,
    title,
    launchTitle: title,
    startedAt: new Date().toISOString(),
    ended: false,
    exitCode: null,
    viewers: 0,
    columns: 120,
    rows: 32,
    terminal: `/api/v1/sessions/${id}/terminal`,
    memory: null,
    project: 'starstats',
    agent: 'claude',
    mode: null,
    task: null,
    taskId: null,
    command: null,
    lastOutputAt: null,
    kind: 'launch',
    ...over,
  };
}

const kept = () => JSON.parse(localStorage.getItem(TABS_KEY) ?? 'null');
const titles = () => [...document.querySelectorAll('[role="tab"] .t')].map((node) => node.textContent);

beforeEach(() => {
  localStorage.clear();
  renamed.length = 0;
  stopped.length = 0;
  hosted = [session('aaaa0001', 'fix-tests'), session('bbbb0002', 'docs'), session('cccc0003', 'release', { ended: true })];
  studio.sessions = hosted;
  studio.loaded = true;
});

afterEach(() => {
  document.body.innerHTML = '';
});

describe('Terminal Focus tabs', () => {
  it('shows the tabs in the order this browser kept, leaving off hidden and cleared ones', () => {
    localStorage.setItem(TABS_KEY, JSON.stringify({ order: ['cccc0003', 'bbbb0002'], hidden: ['aaaa0001'], cleared: [] }));

    render(TerminalFocus, {});

    expect(titles()).toEqual(['release', 'docs']);
    expect(screen.getByRole('button', { name: '1 hidden' })).toBeTruthy();
  });

  it('keeps a new order and a cleared tab in this browser', async () => {
    render(TerminalFocus, {});

    await fireEvent.keyDown(screen.getByRole('tab', { name: /fix-tests/ }), { key: 'ArrowRight', ctrlKey: true, shiftKey: true });
    await tick();
    expect(titles()).toEqual(['docs', 'fix-tests', 'release']);
    expect(kept().order).toEqual(['bbbb0002', 'aaaa0001', 'cccc0003']);

    await fireEvent.click(screen.getByRole('button', { name: 'Close release' }));
    await tick();
    expect(titles()).toEqual(['docs', 'fix-tests']);
    expect(kept().cleared).toEqual(['cccc0003']);
    expect(stopped).toEqual([]);
  });

  it('lets go of ids the daemon no longer has', async () => {
    localStorage.setItem(TABS_KEY, JSON.stringify({ order: ['gone0000', 'bbbb0002'], hidden: ['gone0001'], cleared: ['gone0002'] }));

    render(TerminalFocus, {});
    await tick();

    expect(kept()).toEqual({ order: ['bbbb0002'], hidden: [], cleared: [] });
  });

  it('brings back a hidden tab the address names', async () => {
    localStorage.setItem(TABS_KEY, JSON.stringify({ order: [], hidden: ['bbbb0002'], cleared: [] }));

    render(TerminalFocus, { id: 'bbbb0002' });
    await tick();

    expect(titles()).toContain('docs');
    expect(kept().hidden).toEqual([]);
  });

  it('sends a new name and an end to the daemon, which every browser reads', async () => {
    render(TerminalFocus, {});

    await fireEvent.keyDown(screen.getByRole('tab', { name: /docs/ }), { key: 'F2' });
    const field = screen.getByRole('textbox');
    await fireEvent.input(field, { target: { value: 'release notes' } });
    await fireEvent.keyDown(field, { key: 'Enter' });
    await vi.waitFor(() => expect(renamed).toEqual([['bbbb0002', 'release notes']]));

    await fireEvent.keyDown(screen.getByRole('tab', { name: /docs/ }), { key: 'Delete' });
    await fireEvent.click(screen.getByRole('button', { name: /End session…/ }));
    await tick();
    await fireEvent.click(screen.getByRole('button', { name: /^End session$/ }));
    await vi.waitFor(() => expect(stopped).toEqual(['bbbb0002']));

    expect(kept()?.cleared ?? []).toEqual([]);
  });
});
