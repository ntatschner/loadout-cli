<script lang="ts">
  // One hosted session: a slim header, its live terminal filling the page,
  // and a panel of details that folds away. The terminal streams over a
  // WebSocket from the daemon; closing this page ends nothing, and a
  // connection that drops is tried again while the session is still there.
  import { onMount, tick } from 'svelte';
  import Command from '../lib/components/Command.svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import Terminal from '../lib/components/Terminal.svelte';
  import { sessions as sessionsApi, terminalAddress, type Session } from '../lib/api';
  import { command } from '../lib/commands';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';
  import { resumeCommand, terminalState } from '../lib/terminal';

  let { id }: { id: string } = $props();

  const line = $derived(studio.lines.find((one) => one.kind === 'session' && one.ref === id));
  const session: Session | undefined = $derived(line?.session);

  let screen = $state<ReturnType<typeof Terminal> | undefined>();
  let toggle = $state<HTMLButtonElement | undefined>();
  let holds = $state(true);
  let connected = $state(false);
  let dropped = $state(false);
  let exited = $state<number | null | undefined>(undefined);
  let lost = $state(false);
  let lostSession = $state<string | null>(null);
  let ending = $state(false);
  let renderer = $state('');
  // When this page last saw output arrive, which is newer than the list's
  // lastOutputAt: the list is read again only when a session starts, ends or
  // is said to be waiting, not on every line an agent writes.
  let heard = $state<number | null>(null);
  let details = $state(typeof window === 'undefined' || !window.matchMedia('(max-width: 959px)').matches);
  let socket: WebSocket | null = null;
  let retry: ReturnType<typeof setTimeout> | null = null;
  let tries = 0;
  let leaving = false;
  let opened = false;

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

  /** Minutes since the session last wrote, by whichever of the list and this page heard it later; Infinity before either has. */
  function quietFor() {
    const listed = session?.lastOutputAt ? Date.parse(session.lastOutputAt) : null;
    const latest = Math.max(listed ?? 0, heard ?? 0);

    return latest ? Math.max(0, Math.floor((studio.now - latest) / 60000)) : Infinity;
  }

  const agentSession = $derived(session?.agentSession ?? lostSession);
  const encoder = new TextEncoder();

  function send(message: object) {
    if (socket?.readyState === WebSocket.OPEN) {
      socket.send(JSON.stringify(message));
    }
  }

  function type(data: string) {
    if (holds && !studio.watchOnly && socket?.readyState === WebSocket.OPEN) {
      socket.send(encoder.encode(data));
    }
  }

  let size = { columns: 80, rows: 24 };

  function resized(columns: number, rows: number) {
    size = { columns, rows };
    // Only the window holding the input may resize the session; the daemon
    // ignores the others, so sending is harmless either way.
    send({ type: 'resize', columns, rows });
  }

  function connect(shown: Session) {
    if (retry) {
      clearTimeout(retry);
      retry = null;
    }

    socket?.close();
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
    await studio.loadSessions();

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

  onMount(() => () => {
    leaving = true;

    if (retry) {
      clearTimeout(retry);
    }

    socket?.close();
  });

  async function fold() {
    details = !details;
    await tick();
  }

  const megabytes = (bytes: number | null | undefined) => (bytes ? `${(bytes / 1048576).toFixed(0)} MB` : 'not known');
</script>

<a class="back" href="#sessions"><span aria-hidden="true">←</span>Sessions</a>

{#if !line || !session}
  {#if lost}
    <div class="ls-session lost">
      <div class="shead"><div class="who"><h1>A session that ended with the daemon</h1></div></div>
      <div class="lostcard" role="status">
        <p>This session ended when the daemon restarted. Its transcript is kept.</p>
        <Command why="Carry it on in a terminal:" command={resumeCommand(lostSession, null)} />
      </div>
    </div>
  {:else}
    <p class="calm">That session has ended and gone. Sessions survive a reload of this page, not a restart of the daemon. <a href="#sessions">Back to Sessions</a></p>
  {/if}
{:else}
  <div class="ls-session">
    <div class="shead">
      <div class="who">
        <h1>{line.title}</h1>
        {#if session.kind === 'command'}
          <span class="facts1">a command that asks as it goes · {line.mins ?? 0} min</span>
        {:else}
          <span class="facts1">{[session.project, session.agent ?? 'agent', session.mode ?? 'mode by the task', `${line.mins ?? 0} min`].filter(Boolean).join(' · ')}</span>
        {/if}
      </div>
      {#if !holds && !studio.watchOnly && connected}
        <HaloButton label="Type here instead" size="small" cmd="another window holds the input · takes it over" onpress={() => send({ type: 'takeover' })} />
      {/if}
      <button
        bind:this={toggle}
        type="button"
        class="ls-halo small"
        aria-expanded={details ? 'true' : 'false'}
        aria-controls="session-details"
        onclick={() => void fold()}
      ><span class="ring" aria-hidden="true"></span>{details ? 'Hide details' : 'Details'}</button>
    </div>

    <Terminal
      bind:this={screen}
      title={line.title}
      project={session.project}
      agent={session.kind === 'command' ? 'loadout' : session.agent}
      {status}
      code={exited ?? session.exitCode}
      {agentSession}
      {holds}
      screenReader={studio.about?.screenReader ?? false}
      leaveTo={toggle}
      oninput={type}
      onresize={resized}
      onretry={() => void reconnect()}
      ondrawn={(drawn) => (renderer = drawn)}
    />

    <aside class="sside" id="session-details" aria-label="About this session" hidden={!details}>
      <section>
        <h2>Facts</h2>
        <dl>
          {#if session.kind !== 'command'}
            <dt>Project</dt><dd>{session.project ?? 'not known'}</dd>
            <dt>Agent</dt><dd>{session.agent ?? 'the project’s default'}</dd>
            <dt>Mode</dt><dd>{session.mode ?? 'chosen by the task'}</dd>
            <dt>Task</dt><dd>{session.taskId ?? session.task ?? 'none'}</dd>
          {/if}
          <dt>Running</dt><dd>{line.mins ?? 0} min</dd>
          <dt>Quiet</dt><dd>{line.quiet ?? 0} min</dd>
          <dt>Memory</dt><dd>{megabytes(session.memory)}</dd>
          <dt>Windows</dt><dd>{session.viewers}</dd>
          <dt>Drawn with</dt><dd>{renderer || '…'}</dd>
        </dl>
      </section>
      {#if line.events?.length}
        <section>
          <h2>Last events</h2>
          <ul>
            {#each line.events.slice(0, 5) as event, index (index)}
              <li><b>{event.t ?? ''}</b>{event.text}</li>
            {/each}
          </ul>
        </section>
      {/if}
      <section>
        <h2>Actions</h2>
        {#if session.command}<Command why="Started with:" command={`$ ${session.command}`} />{/if}
        {#if session.kind !== 'command'}
          <Command why="Carry this conversation on in a terminal instead:" command={resumeCommand(agentSession, session.project)} />
        {/if}
        <p class="calm small">Sessions survive a reload of this page, not a restart of the daemon.</p>
        {#if !session.ended && exited === undefined}
          <HaloButton
            label="End the session"
            kind="danger"
            size="small"
            cmd={command.endSession()}
            disabledReason={studio.watchOnly ? 'Studio is only watching: end it from where it runs' : undefined}
            onpress={() => (ending = true)}
          />
        {/if}
      </section>
    </aside>
  </div>
{/if}

{#if ending && session}
  <Dialog
    title="End this session?"
    why={`This stops the agent in this terminal, and everything it started. No loadout command does this. Its conversation is kept, so it can be carried on with ${resumeCommand(agentSession, session.project).slice(2)}.`}
    danger
    actions={[{ label: 'End it', kind: 'danger', cmd: command.endSession(), onpress: () => void studio.endSession(session).then((ok) => ok && router.go('sessions')) }]}
    onclose={() => (ending = false)}
  />
{/if}
