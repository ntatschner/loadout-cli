<script>
  import { onMount } from 'svelte';
  import { Terminal } from '@xterm/xterm';
  import { FitAddon } from '@xterm/addon-fit';
  import '@xterm/xterm/css/xterm.css';
  import { terminalAddress } from './api.js';

  /** @type {{ session: { id: string, title: string, terminal: string } }} */
  let { session } = $props();

  let host;
  let holds = $state(true);
  let ended = $state(/** @type {null | { code: number | null }} */ (null));
  let dropped = $state(/** @type {string | null} */ (null));
  let socket;

  function send(message) {
    if (socket?.readyState === WebSocket.OPEN) {
      socket.send(JSON.stringify(message));
    }
  }

  function takeOver() {
    send({ type: 'takeover' });
  }

  onMount(() => {
    const styles = getComputedStyle(document.documentElement);
    const terminal = new Terminal({
      cursorBlink: false,
      fontFamily: styles.getPropertyValue('--font-mono'),
      theme: {
        background: styles.getPropertyValue('--colour-terminal-background'),
        foreground: styles.getPropertyValue('--colour-terminal-foreground'),
      },
    });
    const fit = new FitAddon();
    terminal.loadAddon(fit);
    terminal.open(host);
    fit.fit();

    socket = new WebSocket(terminalAddress(session));
    socket.binaryType = 'arraybuffer';

    socket.addEventListener('open', () => {
      send({ type: 'resize', columns: terminal.cols, rows: terminal.rows });
    });

    socket.addEventListener('message', (event) => {
      if (typeof event.data !== 'string') {
        terminal.write(new Uint8Array(event.data));

        return;
      }

      const message = JSON.parse(event.data);

      if (message.type === 'lease') {
        holds = message.holds;
      } else if (message.type === 'exit') {
        ended = { code: message.code };
      } else if (message.type === 'dropped') {
        dropped = message.reason;
      }
    });

    const encoder = new TextEncoder();
    const typing = terminal.onData((data) => {
      if (holds && socket.readyState === WebSocket.OPEN) {
        socket.send(encoder.encode(data));
      }
    });

    // Only the window holding the input may resize the session; the
    // daemon ignores the others, so sending is harmless either way.
    const resizing = terminal.onResize(({ cols, rows }) => send({ type: 'resize', columns: cols, rows }));
    const watcher = new ResizeObserver(() => fit.fit());
    watcher.observe(host);

    terminal.focus();

    return () => {
      watcher.disconnect();
      typing.dispose();
      resizing.dispose();
      socket.close();
      terminal.dispose();
    };
  });
</script>

<section class="frame" aria-label={`Terminal: ${session.title}`}>
  <div class="bar">
    <h2>{session.title}</h2>
    <p class="state" role="status">
      {#if ended}
        Ended{ended.code === null ? '' : ` with exit code ${ended.code}`}.
      {:else if dropped}
        Disconnected: {dropped}. Reload to reattach.
      {:else if holds}
        You are typing here.
      {:else}
        Watching. Another window holds the input.
        <button type="button" onclick={takeOver}>Take over</button>
      {/if}
    </p>
  </div>
  <div class="screen" bind:this={host}></div>
</section>

<style>
  .frame {
    display: flex;
    flex-direction: column;
    min-height: 0;
    flex: 1;
    border: 1px solid var(--colour-border-subtle);
    border-radius: var(--radius-medium);
    background: var(--colour-terminal-background);
    overflow: hidden;
  }

  .bar {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: var(--space-2);
    padding: var(--space-2) var(--space-3);
    background: var(--colour-surface-raised);
    border-bottom: 1px solid var(--colour-border-subtle);
  }

  h2 {
    margin: 0;
    font-size: var(--size-body);
  }

  .state {
    margin: 0;
    color: var(--colour-text-muted);
    font-size: var(--size-small);
  }

  .screen {
    flex: 1;
    min-height: 240px;
    padding: var(--space-2);
  }
</style>
