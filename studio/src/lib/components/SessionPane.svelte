<script lang="ts">
  // One hosted session's live terminal and the connection behind it, for any
  // page that shows one: the session's own page, a pane of Terminal Focus,
  // Minimal's focus frame. Each opens its own WebSocket from
  // terminalAddress(), as the session page always has. A pane that stops
  // being shown is removed, which closes its socket; shown again, it opens
  // a new one and the daemon replays the scrollback. Typing goes only to
  // the terminal that has the keyboard, and Studio never types into a
  // session for you.
  import { onMount } from 'svelte';
  import StatusStrip from './StatusStrip.svelte';
  import Terminal from './Terminal.svelte';
  import { sessions as sessionsApi, terminalAddress, type Session } from '../api';
  import { studio } from '../state.svelte';
  import { terminalState, type TerminalState } from '../terminal';
  import type { PaneHandle } from '../modes';

  interface Props {
    id: string;
    /** Where Ctrl+] sends focus. */
    leaveTo?: HTMLElement;
    /** Told whenever what the terminal says about the session changes. */
    onstatus?: (status: TerminalState, facts: { agentSession: string | null; holds: boolean; renderer: string }) => void;
    /** Told when output arrives, for a tab's new-output dot. */
    onoutput?: () => void;
    /** Told when you type into it, for which pane has been idle longest. */
    ontyped?: () => void;
    /** Given what a deck needs to reach this pane once it is on the page, and null as it goes. */
    onready?: (pane: PaneHandle | null) => void;
  }

  let { id, leaveTo = undefined, onstatus = undefined, onoutput = undefined, ontyped = undefined, onready = undefined }: Props = $props();

  const line = $derived(studio.lines.find((one) => one.kind === 'session' && one.ref === id));
  const session: Session | undefined = $derived(line?.session);

  let screen = $state<ReturnType<typeof Terminal> | undefined>();
  let holds = $state(true);
  let connected = $state(false);
  let dropped = $state(false);
  let exited = $state<number | null | undefined>(undefined);
  let lost = $state(false);
  let lostSession = $state<string | null>(null);
  let renderer = $state('');
  // When this pane last saw output arrive, which is newer than the list's
  // lastOutputAt: the list is read again only when a session starts, ends or
  // is said to be waiting, not on every line an agent writes.
  let heard = $state<number | null>(null);
  let socket: WebSocket | null = null;
  let retry: ReturnType<typeof setTimeout> | null = null;
  let tries = 0;
  let leaving = false;

  const status = $derived(
    terminalState({
      session,
      connected,
      dropped,
      exited,
      lost,
      watchOnly: studio.watchOnly,
      quiet: quietFor(),
      idleAfter: studio.idleAfter,
    }),
  );

  /** Minutes since the session last wrote, by whichever of the list and this pane heard it later; Infinity before either has. */
  function quietFor() {
    const listed = session?.lastOutputAt ? Date.parse(session.lastOutputAt) : null;
    const latest = Math.max(listed ?? 0, heard ?? 0);

    return latest ? Math.max(0, Math.floor((studio.now - latest) / 60000)) : Infinity;
  }

  const agentSession = $derived(session?.agentSession ?? lostSession);

  $effect(() => {
    onstatus?.(status, { agentSession, holds, renderer });
  });

  const encoder = new TextEncoder();

  /** Sends a control message: resize or take over. */
  export function send(message: object) {
    if (socket?.readyState === WebSocket.OPEN) {
      socket.send(JSON.stringify(message));
    }
  }

  export function focus() {
    screen?.focus();
  }

  export function find(term: string, backwards = false): boolean {
    return screen?.find(term, backwards) ?? false;
  }

  export function clearFind() {
    screen?.clearFind();
  }

  function type(data: string) {
    if (holds && !studio.watchOnly && socket?.readyState === WebSocket.OPEN) {
      socket.send(encoder.encode(data));
      ontyped?.();
    }
  }

  let size = { columns: 80, rows: 24 };

  function resized(columns: number, rows: number) {
    size = { columns, rows };
    // Only the window holding the input may resize the session; the daemon
    // ignores the others, so sending is harmless either way.
    send({ type: 'resize', columns, rows });
  }

  function disconnect() {
    if (retry) {
      clearTimeout(retry);
      retry = null;
    }

    const closing = socket;
    socket = null;
    connected = false;
    closing?.close();
  }

  function connect(shown: Session) {
    disconnect();
    const opening = new WebSocket(terminalAddress(shown));
    opening.binaryType = 'arraybuffer';
    socket = opening;

    opening.addEventListener('open', () => {
      if (socket !== opening) {
        return;
      }

      connected = true;
      dropped = false;
      tries = 0;
      send({ type: 'resize', ...size });
    });

    opening.addEventListener('close', () => {
      if (socket !== opening) {
        return;
      }

      connected = false;

      if (leaving || exited !== undefined) {
        return;
      }

      // The output so far stays on the screen, and reopening replays the
      // scrollback, so trying again loses nothing.
      dropped = true;
      retry = setTimeout(() => void reconnect(), Math.min(10_000, 1000 * 2 ** tries++));
    });

    opening.addEventListener('message', (event) => {
      if (typeof event.data !== 'string') {
        screen?.write(new Uint8Array(event.data as ArrayBuffer));
        onoutput?.();

        // Kept coarse, so a busy agent does not redraw the page per line.
        const now = Date.now();

        if (!heard || now - heard > 5000) {
          heard = now;
        }

        return;
      }

      const message = JSON.parse(event.data) as { type: string; holds?: boolean; code?: number | null };

      if (message.type === 'lease') {
        holds = !!message.holds;
      } else if (message.type === 'exit') {
        exited = message.code ?? null;
        void studio.loadSessions();
      }
    });
  }

  /** Again, after a drop: the session may have gone with the daemon, which a fresh list says. */
  async function reconnect() {
    try {
      await studio.loadSessions();
    } catch {
      // The daemon is not answering yet, as while it restarts: try again later.
      retry = setTimeout(() => void reconnect(), Math.min(10_000, 1000 * 2 ** tries++));

      return;
    }

    if (session && !session.ended) {
      // A reconnect replays the scrollback, so the screen starts clean.
      screen?.write('\x1bc');
      connect(session);
    } else if (!session) {
      await checkLost();
    }
  }

  async function checkLost() {
    const gone = await sessionsApi.gone(id).catch(() => ({ lost: false, agentSession: null }));

    lost = gone.lost;
    lostSession = gone.agentSession;
  }

  let opened = false;

  $effect(() => {
    // Connect once the session is known and the terminal is on the page.
    if (!opened && screen && session) {
      opened = true;
      connect(session);
    }
  });

  $effect(() => {
    if (studio.loaded && !line && !lost) {
      void checkLost();
    }
  });

  onMount(() => {
    onready?.({ focus, find, clearFind });

    return () => {
      leaving = true;
      onready?.(null);
      disconnect();
    };
  });
</script>

<Terminal
  bind:this={screen}
  title={line?.title ?? id}
  project={session?.project ?? null}
  agent={session?.kind === 'command' ? 'loadout' : (session?.agent ?? null)}
  {status}
  code={exited ?? session?.exitCode ?? null}
  {agentSession}
  {holds}
  screenReader={studio.about?.screenReader ?? false}
  {leaveTo}
  oninput={type}
  onresize={resized}
  onretry={() => void reconnect()}
  ondrawn={(drawn) => (renderer = drawn)}
  foot={session?.status && !session.ended ? statusFoot : undefined}
/>

{#snippet statusFoot()}
  {#if session?.status}<StatusStrip status={session.status} now={studio.now} />{/if}
{/snippet}
