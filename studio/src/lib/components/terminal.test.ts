// Terminal: the bar under the screen says what the session needs in words,
// typing follows the state, Ctrl+] leaves for the page, the screen is
// themed from the term-* tokens with the contrast floor the README sets, and
// a screen resized over and over tells its session once.
// xterm.js cannot draw in a document without layout, so it is replaced by a
// recorder that keeps what Terminal asked of it.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';
import { readFileSync } from 'node:fs';
import { SPLIT_SETTLED } from '../splits';

const made: FakeTerminal[] = [];

class FakeTerminal {
  options: Record<string, unknown>;
  cols = 80;
  rows = 24;
  textarea = document.createElement('textarea');
  keys: ((event: KeyboardEvent) => boolean) | null = null;
  focused = 0;
  host: HTMLElement | null = null;
  resized: ((size: { cols: number; rows: number }) => void) | null = null;
  selection = '';
  pasted: string[] = [];

  constructor(options: Record<string, unknown>) {
    this.options = { ...options };
    made.push(this);
  }

  loadAddon(addon: { recordTerminal?: (terminal: FakeTerminal) => void }) {
    addon.recordTerminal?.(this);
  }
  open(host: HTMLElement) {
    this.host = host;
    host.appendChild(this.textarea);
  }
  attachCustomKeyEventHandler(handler: (event: KeyboardEvent) => boolean) {
    this.keys = handler;
  }
  onData() {
    return { dispose() {} };
  }
  onResize(listener: (size: { cols: number; rows: number }) => void) {
    this.resized = listener;

    return { dispose() {} };
  }
  resize(cols: number, rows: number) {
    this.cols = cols;
    this.rows = rows;
    this.resized?.({ cols, rows });
  }
  write() {}
  focus() {
    this.focused++;
  }
  scrollToBottom() {}
  hasSelection() {
    return this.selection !== '';
  }
  getSelection() {
    return this.selection;
  }
  clearSelection() {
    this.selection = '';
  }
  paste(text: string) {
    this.pasted.push(text);
  }
  dispose() {}
}

/** Puts a clipboard on navigator, which jsdom has none of, holding what is written to it. */
function stubClipboard(board: { held?: string; refuse?: boolean } = {}) {
  const writes: string[] = [];
  const clipboard = {
    writeText: vi.fn(async (text: string) => {
      if (board.refuse) {
        throw new DOMException('Write permission denied.', 'NotAllowedError');
      }

      writes.push(text);
    }),
    readText: vi.fn(async () => {
      if (board.refuse) {
        throw new DOMException('Read permission denied.', 'NotAllowedError');
      }

      return board.held ?? '';
    }),
  };
  Object.defineProperty(navigator, 'clipboard', { value: clipboard, configurable: true });

  return { clipboard, writes };
}

/** Lets the clipboard's promises settle and the component draw what they changed. */
async function settle() {
  for (let i = 0; i < 5; i++) {
    await Promise.resolve();
  }

  await tick();
}

vi.mock('@xterm/xterm', () => ({ Terminal: FakeTerminal }));
// Proposes as xterm does: as many whole-pixel cells, about 0.6em each, as go
// into 605 pixels at the text size the terminal has now.
vi.mock('@xterm/addon-fit', () => ({
  FitAddon: class {
    terminal: { options: Record<string, unknown> } | null = null;
    recordTerminal(terminal: { options: Record<string, unknown> }) {
      this.terminal = terminal;
    }
    fit() {}
    proposeDimensions() {
      const size = Number(this.terminal?.options.fontSize ?? 13);

      return { cols: Math.floor(605 / Math.floor(size * 0.6)), rows: 24 };
    }
  },
}));
vi.mock('@xterm/addon-webgl', () => ({ WebglAddon: class { onContextLoss() {} dispose() {} } }));
vi.mock('@xterm/xterm/css/xterm.css', () => ({}));

globalThis.ResizeObserver ??= class { observe() {} disconnect() {} } as unknown as typeof ResizeObserver;
window.matchMedia ??= ((query: string) => ({ matches: false, media: query, addEventListener() {}, removeEventListener() {} })) as unknown as typeof window.matchMedia;

const { default: Terminal } = await import('./Terminal.svelte');

afterEach(() => {
  made.length = 0;
  Reflect.deleteProperty(navigator, 'clipboard');
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

  // xterm turned Ctrl+V into ^V and cancelled the key, so the browser never
  // fired its paste event and nothing was pasted.
  it.each([
    ['Ctrl+V', { key: 'v', code: 'KeyV', ctrlKey: true }],
    ['Ctrl+Shift+V', { key: 'V', code: 'KeyV', ctrlKey: true, shiftKey: true }],
  ])('leaves %s to the browser, so its own paste event fires', (_, keys) => {
    render(Terminal, { title: 'starstats', status: 'working' });

    const event = new KeyboardEvent('keydown', { ...keys, cancelable: true });
    expect(made[0].keys!(event)).toBe(false);
    expect(event.defaultPrevented).toBe(false);
  });

  it('passes Alt+V to the agent, for Claude Code to paste an image', () => {
    render(Terminal, { title: 'starstats', status: 'working' });

    expect(made[0].keys!(new KeyboardEvent('keydown', { key: 'v', code: 'KeyV', altKey: true }))).toBe(true);
  });

  it('copies a selection on Ctrl+C and clears it, rather than interrupting', async () => {
    const { writes } = stubClipboard();
    render(Terminal, { title: 'starstats', status: 'working' });
    made[0].selection = 'dotnet test';

    expect(made[0].keys!(new KeyboardEvent('keydown', { key: 'c', code: 'KeyC', ctrlKey: true, cancelable: true }))).toBe(false);
    await settle();

    expect(writes).toEqual(['dotnet test']);
    expect(made[0].hasSelection()).toBe(false);
  });

  it('passes Ctrl+C to the agent as the interrupt when nothing is selected', async () => {
    const { clipboard } = stubClipboard();
    render(Terminal, { title: 'starstats', status: 'working' });

    expect(made[0].keys!(new KeyboardEvent('keydown', { key: 'c', code: 'KeyC', ctrlKey: true }))).toBe(true);
    await settle();
    expect(clipboard.writeText).not.toHaveBeenCalled();
  });

  it('keeps the browser menu off the terminal on right-click', () => {
    stubClipboard();
    render(Terminal, { title: 'starstats', status: 'working' });

    const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
    made[0].host!.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
  });

  it('stops taking right-clicks once it has gone', () => {
    stubClipboard();
    const { unmount } = render(Terminal, { title: 'starstats', status: 'working' });
    const host = made[0].host!;
    unmount();

    const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
    host.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(false);
  });

  it('copies a selection on right-click and clears it', async () => {
    const { writes } = stubClipboard({ held: 'not this' });
    render(Terminal, { title: 'starstats', status: 'working' });
    made[0].selection = 'exit code 3';

    made[0].host!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
    await settle();

    expect(writes).toEqual(['exit code 3']);
    expect(made[0].hasSelection()).toBe(false);
    expect(made[0].pasted).toEqual([]);
  });

  it('pastes the clipboard through xterm on right-click when nothing is selected', async () => {
    const { clipboard } = stubClipboard({ held: 'git status' });
    render(Terminal, { title: 'starstats', status: 'working' });

    made[0].host!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
    await settle();

    expect(made[0].pasted).toEqual(['git status']);
    expect(clipboard.writeText).not.toHaveBeenCalled();
  });

  it('says on the bar when the browser refuses the clipboard', async () => {
    stubClipboard({ refuse: true });
    render(Terminal, { title: 'starstats', status: 'working' });
    expect(screen.queryByRole('alert')).toBeNull();

    made[0].host!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
    await settle();

    expect(screen.getByRole('alert').textContent).toBe('The browser refused the clipboard');
    expect(made[0].pasted).toEqual([]);
  });

  it('says on the bar when the browser has no clipboard to offer', async () => {
    render(Terminal, { title: 'starstats', status: 'working' });
    made[0].selection = 'exit code 3';

    made[0].keys!(new KeyboardEvent('keydown', { key: 'c', code: 'KeyC', ctrlKey: true }));
    await settle();

    expect(screen.getByRole('alert').textContent).toBe('The browser refused the clipboard');
  });

  it('names the paste, image and copy keys on the bar', () => {
    render(Terminal, { title: 'starstats', status: 'working' });

    expect(screen.getByText('Ctrl+V paste · Alt+V image · right-click copy or paste')).toBeTruthy();
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

  // FitAddon takes the box xterm is opened in as the room it has. Padding on
  // that box (every element in Studio is border-box) made it ask for more
  // than there was, and the screen drew scrollbars beside xterm's own.
  it('opens xterm in a box of its own inside the screen, and pads only the screen', () => {
    render(Terminal, { title: 'starstats', status: 'working' });

    const host = made[0].host!;
    expect(host.classList.contains('mount')).toBe(true);
    expect(host.parentElement!.classList.contains('screen')).toBe(true);

    const css = (readFileSync('src/styles/components.css', 'utf-8') + readFileSync('src/styles/studio.css', 'utf-8')).replace(/\/\*[\s\S]*?\*\//g, '');
    const rules = (selector: RegExp) => [...css.matchAll(/([^{}]+)\{([^}]*)\}/g)].filter(([, s]) => selector.test(s)).map(([, , body]) => body);
    expect(rules(/\.mount\b/).join(' ')).not.toMatch(/padding/);
    expect(rules(/\.mount\b/).join(' ')).toMatch(/overflow-y: hidden/);
    expect(rules(/^\s*\.ls-term \.screen\s*$/).join(' ')).toMatch(/padding: 8px 10px/);
    expect(rules(/\.screen\s*$/).join(' ')).not.toMatch(/overflow: auto/);
  });

  it('under Fit takes the largest text size at which xterm says 80 columns go in', async () => {
    render(Terminal, { title: 'starstats', status: 'working' });

    await fireEvent.click(screen.getByRole('button', { name: 'Fit' }));

    // 13.3px makes 7-pixel cells, 560 of the 605; 13.4px would make 8, 640.
    expect(made[0].options.fontSize).toBe(13.3);
    expect(made[0].cols).toBe(80);
    expect(made[0].rows).toBe(24);
  });

  it('tells the session a new size once it stops changing, not on every frame', async () => {
    vi.useFakeTimers();

    try {
      const onresize = vi.fn();
      render(Terminal, { title: 'starstats', status: 'working', onresize });
      onresize.mockClear();

      for (let cols = 81; cols <= 120; cols++) {
        made[0].resize(cols, 30);
        vi.advanceTimersByTime(16);
      }

      expect(onresize).not.toHaveBeenCalled();
      vi.advanceTimersByTime(200);
      expect(onresize).toHaveBeenCalledOnce();
      expect(onresize).toHaveBeenCalledWith(120, 30);
    } finally {
      vi.useRealTimers();
    }
  });

  it('tells the session as soon as a divider is let go and the screen has taken its last size', async () => {
    vi.useFakeTimers();

    try {
      const onresize = vi.fn();
      render(Terminal, { title: 'starstats', status: 'working', onresize });
      onresize.mockClear();

      made[0].resize(100, 30);
      document.dispatchEvent(new CustomEvent(SPLIT_SETTLED));
      // The resize the release caused, fitted in the frame after.
      vi.advanceTimersByTime(16);
      made[0].resize(101, 30);
      vi.advanceTimersByTime(40);
      expect(onresize).toHaveBeenCalledOnce();
      expect(onresize).toHaveBeenCalledWith(101, 30);

      // Nothing waits behind it, and a divider let go with no change says nothing.
      vi.advanceTimersByTime(500);
      document.dispatchEvent(new CustomEvent(SPLIT_SETTLED));
      expect(onresize).toHaveBeenCalledOnce();
    } finally {
      vi.useRealTimers();
    }
  });
});
