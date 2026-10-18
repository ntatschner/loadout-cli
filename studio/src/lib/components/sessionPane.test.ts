// SessionPane with its socket: the one connection a shown pane opens, closed
// as the pane leaves the page, and a reconnect that waits for a daemon that
// is not answering rather than failing. xterm.js is the recorder the
// terminal's own tests use; the socket is a stand-in that records itself.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render } from '@testing-library/svelte';
import { tick } from 'svelte';

class FakeTerminal {
  options: Record<string, unknown>;
  cols = 80;
  rows = 24;
  textarea = document.createElement('textarea');

  constructor(options: Record<string, unknown>) {
    this.options = { ...options };
  }

  loadAddon() {}
  open(host: HTMLElement) {
    host.appendChild(this.textarea);
  }
  attachCustomKeyEventHandler() {}
  onData() {
    return { dispose() {} };
  }
  onResize() {
    return { dispose() {} };
  }
  resize() {}
  write() {}
  focus() {}
  scrollToBottom() {}
  clearSelection() {}
  dispose() {}
}

vi.mock('@xterm/xterm', () => ({ Terminal: FakeTerminal }));
vi.mock('@xterm/addon-fit', () => ({ FitAddon: class { fit() {} proposeDimensions() { return { cols: 80, rows: 24 }; } } }));
vi.mock('@xterm/addon-webgl', () => ({ WebglAddon: class { onContextLoss() {} dispose() {} } }));
vi.mock('@xterm/addon-search', () => ({ SearchAddon: class { findNext() { return false; } findPrevious() { return false; } clearDecorations() {} } }));
vi.mock('@xterm/xterm/css/xterm.css', () => ({}));

globalThis.ResizeObserver ??= class { observe() {} disconnect() {} } as unknown as typeof ResizeObserver;
window.matchMedia ??= ((query: string) => ({ matches: false, media: query, addEventListener() {}, removeEventListener() {} })) as unknown as typeof window.matchMedia;

const sockets: FakeSocket[] = [];

class FakeSocket extends EventTarget {
  static OPEN = 1;
  readyState = 0;
  binaryType = 'blob';
  closed = false;

  constructor(public url: string) {
    super();
    sockets.push(this);
  }

  send() {}

  close() {
    this.closed = true;
    this.readyState = 3;
  }

  /** The daemon dropping the connection. */
  drop() {
    this.readyState = 3;
    this.dispatchEvent(new Event('close'));
  }
}

const { default: SessionPane } = await import('./SessionPane.svelte');
const { studio } = await import('../state.svelte');

const session = {
  id: 'abc',
  title: 'fix-tests',
  startedAt: new Date().toISOString(),
  ended: false,
  exitCode: null,
  viewers: 0,
  columns: 80,
  rows: 24,
  terminal: '/api/v1/sessions/abc/terminal',
  memory: null,
  project: 'starstats',
  agent: 'claude',
  mode: null,
  task: null,
  taskId: null,
  command: null,
  lastOutputAt: null,
};

beforeEach(() => {
  sockets.length = 0;
  vi.stubGlobal('WebSocket', FakeSocket);
  studio.sessions = [session];
  studio.loaded = true;
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
  vi.restoreAllMocks();
  studio.sessions = [];
});

describe('SessionPane', () => {
  it('opens one socket while it is shown, and closes it as it leaves the page', async () => {
    const { unmount } = render(SessionPane, { id: 'abc' });
    await tick();

    expect(sockets.length).toBe(1);
    expect(sockets[0].url).toContain('/api/v1/sessions/abc/terminal');
    expect(sockets[0].closed).toBe(false);

    unmount();

    expect(sockets[0].closed).toBe(true);
  });

  it('waits for a daemon that is not answering, and tries again rather than failing', async () => {
    vi.useFakeTimers();
    const read = vi.spyOn(studio, 'loadSessions').mockRejectedValue(new TypeError('Failed to fetch'));
    render(SessionPane, { id: 'abc' });
    await tick();

    sockets[0].drop();
    await vi.advanceTimersByTimeAsync(1000);
    expect(read).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(2000);
    expect(read).toHaveBeenCalledTimes(2);

    // Answering again, it opens a new connection.
    read.mockResolvedValue(undefined);
    await vi.advanceTimersByTimeAsync(4000);
    expect(sockets.length).toBe(2);
  });
});
