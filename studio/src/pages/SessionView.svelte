<script lang="ts">
  // One hosted session: a slim header, its live terminal filling the page,
  // and a panel of details that folds away. The terminal and its connection
  // are SessionPane's, the same one Terminal Focus shows in its panes;
  // closing this page ends nothing.
  import { tick } from 'svelte';
  import Command from '../lib/components/Command.svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import SessionPane from '../lib/components/SessionPane.svelte';
  import { sessions as sessionsApi, type Session } from '../lib/api';
  import { command } from '../lib/commands';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';
  import { resumeCommand, type TerminalState } from '../lib/terminal';

  let { id }: { id: string } = $props();

  const line = $derived(studio.lines.find((one) => one.kind === 'session' && one.ref === id));
  const session: Session | undefined = $derived(line?.session);

  let pane = $state<ReturnType<typeof SessionPane> | undefined>();
  let toggle = $state<HTMLButtonElement | undefined>();
  let status = $state<TerminalState>('connecting');
  let holds = $state(true);
  let renderer = $state('');
  let agentSession = $state<string | null>(null);
  let lost = $state(false);
  let lostSession = $state<string | null>(null);
  let ending = $state(false);
  let details = $state(typeof window === 'undefined' || !window.matchMedia('(max-width: 959px)').matches);

  const ended = $derived(status === 'ended' || status === 'error' || status === 'lost');

  $effect(() => {
    if (studio.loaded && !line && !lost) {
      sessionsApi.gone(id).then((gone) => {
        lost = gone.lost;
        lostSession = gone.agentSession;
      }, () => undefined);
    }
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
      {#if !holds && !studio.watchOnly && !ended && status !== 'connecting' && status !== 'reconnecting'}
        <HaloButton label="Type here instead" size="small" cmd="another window holds the input · takes it over" onpress={() => pane?.send({ type: 'takeover' })} />
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

    <SessionPane
      bind:this={pane}
      {id}
      leaveTo={toggle}
      onstatus={(now, facts) => {
        status = now;
        holds = facts.holds;
        renderer = facts.renderer;
        agentSession = facts.agentSession;
      }}
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
        {#if !session.ended && !ended}
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
