// Terminal: the bar under the screen says what the session needs in words,
// typing follows the state, Ctrl+] leaves for the page, and the screen is
// themed from the term-* tokens with the contrast floor the README sets.
// xterm.js cannot draw in a document without layout, so it is replaced by a
// recorder that keeps what Terminal asked of it.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';

const made: FakeTerminal[] = [];

class FakeTerminal {
  options: Record<string, unknown>;
  cols = 80;
  rows = 24;
  textarea = document.createElement('textarea');
  keys: ((event: KeyboardEvent) => boolean) | null = null;
  focused = 0;

  constructor(options: Record<string, unknown>) {
    this.options = { ...options };
    made.push(this);
  }

  loadAddon() {}
  open(host: HTMLElement) {
    host.appendChild(this.textarea);
  }
  attachCustomKeyEventHandler(handler: (event: KeyboardEvent) => boolean) {
    this.keys = handler;
  }
  onData() {
    return { dispose() {} };
  }
  onResize() {
    return { dispose() {} };
  }
  resize(cols: number, rows: number) {
    this.cols = cols;
    this.rows = rows;
  }
  write() {}
  focus() {
    this.focused++;
  }
  scrollToBottom() {}
  dispose() {}
}

vi.mock('@xterm/xterm', () => ({ Terminal: FakeTerminal }));
vi.mock('@xterm/addon-fit', () => ({ FitAddon: class { fit() {} proposeDimensions() { return { cols: 80, rows: 24 }; } } }));
vi.mock('@xterm/addon-webgl', () => ({ WebglAddon: class { onContextLoss() {} dispose() {} } }));
vi.mock('@xterm/xterm/css/xterm.css', () => ({}));

globalThis.ResizeObserver ??= class { observe() {} disconnect() {} } as unknown as typeof ResizeObserver;
window.matchMedia ??= ((query: string) => ({ matches: false, media: query, addEventListener() {}, removeEventListener() {} })) as unknown as typeof window.matchMedia;

const { default: Terminal } = await import('./Terminal.svelte');

afterEach(() => {
  made.length = 0;
  document.documentElement.removeAttribute('data-theme');
});

describe('Terminal', () => {
  it('says a session needs approval, sends you to the terminal, and offers no choice', () => {
    render(Terminal, { title: 'starstats', status: 'approval' });

    const bar = screen.getByRole('status');
    expect(bar.textContent).toContain('It is asking for approval. The choices are in the terminal; answer them there.');
    expect(screen.getByRole('button', { name: /Go to the question/ })).toBeTruthy();
    expect(screen.queryByRole('button', { name: /^(Yes|No|Approve|Allow)/ })).toBeNull();
    expect(made[0].options.disableStdin).toBe(false);
  });

  it('turns typing off and offers the resume command once it has ended', async () => {
    render(Terminal, { title: 'starstats', status: 'error', code: 3, agentSession: 'c0ffee00-1111', project: 'starstats' });
    await tick();

    expect(screen.getByRole('status').textContent).toContain('Exited with an error (exit code 3)');
    expect(screen.getByText('$ loadout resume c0ffee00-1111 --project starstats')).toBeTruthy();
    expect(made[0].options.disableStdin).toBe(true);
  });

  it('says nothing under the screen while it works', () => {
    render(Terminal, { title: 'starstats', status: 'working' });

    expect((screen.getByRole('status', { hidden: true }) as HTMLElement).hidden).toBe(true);
  });

  it('leaves for the page on Ctrl+], and keeps every other key for the agent', () => {
    const toggle = document.createElement('button');
    document.body.appendChild(toggle);
    render(Terminal, { title: 'starstats', status: 'working', leaveTo: toggle });

    const keys = made[0].keys!;
    expect(keys(new KeyboardEvent('keydown', { key: ']', code: 'BracketRight', ctrlKey: true }))).toBe(false);
    expect(document.activeElement).toBe(toggle);
    expect(keys(new KeyboardEvent('keydown', { key: 'Tab' }))).toBe(true);
    expect(keys(new KeyboardEvent('keydown', { key: 'ArrowUp' }))).toBe(true);
    toggle.remove();
  });

  it('asks the page around it where to go on Ctrl+], where it was given nowhere', () => {
    const around = document.createElement('div');
    const asked = vi.fn();
    around.addEventListener('leaveterminal', asked);
    document.body.appendChild(around);
    render(Terminal, { target: around, props: { title: 'starstats', status: 'working' } });

    made[0].keys!(new KeyboardEvent('keydown', { key: ']', code: 'BracketRight', ctrlKey: true }));

    expect(asked).toHaveBeenCalledOnce();
    around.remove();
  });

  it('lifts colours to 7:1 in the high-contrast themes and 4.5:1 otherwise, and names the screen', () => {
    render(Terminal, { title: 'starstats', status: 'working' });
    expect(made[0].options.minimumContrastRatio).toBe(4.5);
    expect(made[0].options.lineHeight).toBe(1.4);
    expect(made[0].textarea.getAttribute('aria-label')).toBe('Terminal: starstats');

    document.documentElement.setAttribute('data-theme', 'hc-dark');
    render(Terminal, { title: 'starstats', status: 'working' });
    expect(made[1].options.minimumContrastRatio).toBe(7);
  });

  it('turns on the screen-reader mode only when a screen reader is in use', () => {
    render(Terminal, { title: 'starstats', status: 'working', screenReader: true });
    expect(made[0].options.screenReaderMode).toBe(true);
  });
});
