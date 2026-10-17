<script lang="ts">
  // One hosted session: its live terminal, what it is, and how to carry it
  // on in a terminal instead. The terminal streams over a WebSocket from the
  // daemon; closing this page ends nothing.
  import { onMount } from 'svelte';
  import { Terminal } from '@xterm/xterm';
  import { FitAddon } from '@xterm/addon-fit';
  import { WebglAddon } from '@xterm/addon-webgl';
  import '@xterm/xterm/css/xterm.css';
  import Command from '../lib/components/Command.svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import Signal from '../lib/components/Signal.svelte';
  import { terminalAddress, type Session } from '../lib/api';
  import { command } from '../lib/commands';
  import { router } from '../lib/router.svelte';
  import { wordFor } from '../lib/signals';
  import { studio } from '../lib/state.svelte';

  let { id }: { id: string } = $props();

  const line = $derived(studio.lines.find((one) => one.kind === 'session' && one.ref === id));
  const session: Session | undefined = $derived(line?.session);

  let host = $state<HTMLDivElement | undefined>();
  let holds = $state(true);
  let exited = $state<number | null | undefined>(undefined);
  let dropped = $state<string | null>(null);
  let renderer = $state('');
  let ending = $state(false);
  let socket: WebSocket | null = null;

  function send(message: object) {
    if (socket?.readyState === WebSocket.OPEN) {
      socket.send(JSON.stringify(message));
    }
  }

  function colours() {
    const styles = getComputedStyle(document.documentElement);
    const read = (name: string) => styles.getPropertyValue(name).trim();

    return {
      background: read('--ground'),
      foreground: read('--ink'),
      cursor: read('--accent'),
      selectionBackground: read('--tint-picked') || read('--accent'),
    };
  }

  let attached = false;

  $effect(() => {
    // Attach once the session is known and the box is on the page.
    if (attached || !host || !session) {
      return;
    }

    attached = true;
    attach(host, session);
  });

  let cleanup: (() => void) | null = null;

  function attach(box: HTMLDivElement, shown: Session) {
    const styles = getComputedStyle(document.documentElement);
    const terminal = new Terminal({
      cursorBlink: false,
      fontFamily: styles.getPropertyValue('--font-mono').trim() || 'monospace',
      fontSize: 13,
      theme: colours(),
      allowProposedApi: false,
    });
    const fit = new FitAddon();
    terminal.loadAddon(fit);

    // Before open(), so the DOM renderer is never made: it writes <style>
    // elements and style attributes, which Studio's content security policy
    // refuses. Without WebGL the terminal still works and the policy reports
    // what it blocked.
    try {
      const webgl = new WebglAddon();
      webgl.onContextLoss(() => webgl.dispose());
      terminal.loadAddon(webgl);
      renderer = 'webgl';
    } catch {
      renderer = 'dom';
    }

    terminal.open(box);
    // Named, as every field on the page is, so a browser can tell it apart.
    terminal.textarea?.setAttribute('name', 'terminal-input');
    fit.fit();

    socket = new WebSocket(terminalAddress(shown));
    socket.binaryType = 'arraybuffer';

    socket.addEventListener('open', () => send({ type: 'resize', columns: terminal.cols, rows: terminal.rows }));
    socket.addEventListener('close', () => {
      if (exited === undefined && !dropped) {
        dropped = 'the connection closed';
      }
    });
    socket.addEventListener('message', (event) => {
      if (typeof event.data !== 'string') {
        terminal.write(new Uint8Array(event.data as ArrayBuffer));

        return;
      }

      const message = JSON.parse(event.data) as { type: string; holds?: boolean; code?: number | null; reason?: string };

      if (message.type === 'lease') {
        holds = !!message.holds;
      } else if (message.type === 'exit') {
        exited = message.code ?? null;
        void studio.loadSessions();
      } else if (message.type === 'dropped') {
        dropped = message.reason ?? 'dropped';
      }
    });

    const encoder = new TextEncoder();
    const typing = terminal.onData((data) => {
      if (holds && !studio.watchOnly && socket?.readyState === WebSocket.OPEN) {
        socket.send(encoder.encode(data));
      }
    });

    // Only the window holding the input may resize the session; the daemon
    // ignores the others, so sending is harmless either way.
    const resizing = terminal.onResize(({ cols, rows }) => send({ type: 'resize', columns: cols, rows }));
    const watcher = new ResizeObserver(() => fit.fit());
    watcher.observe(box);

    const themes = new MutationObserver(() => (terminal.options.theme = colours()));
    themes.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'data-accent'] });

    terminal.focus();

    cleanup = () => {
      themes.disconnect();
      watcher.disconnect();
      typing.dispose();
      resizing.dispose();
      socket?.close();
      terminal.dispose();
    };
  }

  onMount(() => () => cleanup?.());

  const megabytes = (bytes: number | null | undefined) => (bytes ? `${(bytes / 1048576).toFixed(0)} MB` : 'not known');
</script>

<a class="back" href="#sessions"><span aria-hidden="true">←</span>Sessions</a>

{#if !line || !session}
  <p class="calm">That session has ended and gone, or the daemon restarted, which ends every session it hosted. <a href="#sessions">Back to Sessions</a></p>
{:else}
  <div class="head">
    <div>
      <h1 class="sesstitle">{line.title}</h1>
      <span class="fact">{session.project ?? ''} · {session.agent ?? 'agent'} · {session.mode ?? 'mode by the task'} · {line.mins ?? 0} min</span>
    </div>
    <Signal word={wordFor(line, studio.idleAfter)} big />
  </div>
  <div class="sess">
    <div class="sessmain">
      <div class="term">
        <div class="bar">
          <span class={session.ended || exited !== undefined ? '' : 'live'}>{session.ended || exited !== undefined ? '■ ended' : '● live'}</span>
          {session.project ?? ''} · {session.agent ?? ''}
          <span class="grow"></span>
          <span class="state" role="status">
            {#if exited !== undefined || session.ended}
              Ended{(exited ?? session.exitCode) === null ? '' : ` with exit code ${exited ?? session.exitCode}`}.
            {:else if dropped}
              Disconnected: {dropped}. Reload to reattach; the session is still the daemon's.
            {:else if studio.watchOnly}
              Watching only: typing goes nowhere.
            {:else if holds}
              Typing here goes into the session.
            {:else}
              Another window holds the input.
              <button type="button" class="takeover" onclick={() => send({ type: 'takeover' })}>Type here instead</button>
            {/if}
          </span>
        </div>
        <div class="screen" bind:this={host} aria-label="Session terminal" role="group"></div>
      </div>
    </div>
    <aside class="sessaside" aria-label="About this session">
      <dl class="facts">
        <dt>Project</dt><dd>{session.project ?? 'not known'}</dd>
        <dt>Agent</dt><dd>{session.agent ?? 'the project’s default'}</dd>
        <dt>Mode</dt><dd>{session.mode ?? 'chosen by the task'}</dd>
        <dt>Task</dt><dd>{session.taskId ?? session.task ?? 'none'}</dd>
        <dt>Running</dt><dd>{line.mins ?? 0} min</dd>
        <dt>Quiet</dt><dd>{line.quiet ?? 0} min</dd>
        <dt>Memory</dt><dd>{megabytes(session.memory)}</dd>
        <dt>Windows</dt><dd>{session.viewers}</dd>
        <dt>Drawn with</dt><dd>{renderer || '…'}</dd>
      </dl>
      {#if session.command}<Command why="Started with:" command={`$ ${session.command}`} />{/if}
      <Command why="Carry this conversation on in a terminal instead:" command={command.resumeLast()} />
      {#if !session.ended}
        <HaloButton
          label="End the session"
          kind="danger"
          size="small"
          cmd="stops the agent and everything it started · asks first"
          disabledReason={studio.watchOnly ? 'Studio is only watching: end it from where it runs' : undefined}
          onpress={() => (ending = true)}
        />
      {/if}
    </aside>
  </div>
{/if}

{#if ending && session}
  <Dialog
    title="End this session?"
    why="The agent stops, and so does everything it started. Its conversation is kept, so it can be carried on in a terminal with loadout resume --last."
    danger
    actions={[{ label: 'End it', kind: 'danger', cmd: 'ends the session here · changes nothing else', onpress: () => void studio.endSession(session).then((ok) => ok && router.go('sessions')) }]}
    onclose={() => (ending = false)}
  />
{/if}
