<script lang="ts">
  // Sessions: what the daemon is hosting, running and waiting first, then
  // the ones that have ended. Every one is the daemon's, so closing the tab
  // ends nothing; a daemon restart ends them all, which the page says, and
  // offers each one back. Below, every past conversation from both agents,
  // to carry on: resuming is starting a session, so it is here.
  import { onMount } from 'svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import ConversationList from '../lib/components/ConversationList.svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import type { Conversation } from '../lib/api';
  import { resumeAllWords } from '../lib/resume';
  import { router } from '../lib/router.svelte';
  import Signal from '../lib/components/Signal.svelte';
  import Command from '../lib/components/Command.svelte';
  import { wordFor } from '../lib/signals';
  import { studio } from '../lib/state.svelte';
  import { command } from '../lib/commands';

  let { onlaunch, onresume = undefined }: { onlaunch: () => void; onresume?: (conversation: Conversation) => void } = $props();

  let agent = $state('');
  let project = $state('');
  let resumingAll = $state(false);
  let busy = $state(false);

  const agents = $derived([...new Set(studio.conversations.map((one) => one.agent))].sort());
  const projects = $derived([...new Set(studio.conversations.map((one) => one.project).filter((one): one is string => !!one))].sort());
  const shownHistory = $derived(
    studio.conversations.filter((one) => (!agent || one.agent === agent) && (!project || one.project === project)),
  );
  const lost = $derived(studio.lost.filter((one) => one.conversation));

  // Read again on arrival: a conversation started in a terminal says nothing
  // on the event stream, which only hears about Studio's own sessions. A
  // failed read leaves the last one shown.
  onMount(() => void studio.loadHistory().catch(() => undefined));

  async function resumeLost(one: (typeof lost)[number]) {
    busy = true;

    try {
      const id = await studio.resume(one.conversation!, {}, one.id);

      if (id && lost.length <= 1) {
        router.go('session', id);
      }
    } finally {
      busy = false;
    }
  }

  async function resumeAll() {
    busy = true;

    try {
      // One after another, each asked about on its own where it has to be:
      // a restart's sessions are as likely to have been reopened in a
      // terminal since as any other.
      for (const one of [...lost]) {
        await studio.resume(one.conversation!, {}, one.id);
      }
    } finally {
      busy = false;
    }
  }

  const all = $derived(studio.lines.filter((line) => line.kind === 'session'));
  const live = $derived(all.filter((line) => line.stage !== 4));
  const ended = $derived(all.filter((line) => line.stage === 4));

  const unable = $derived(
    studio.watchOnly
      ? studio.watchOnlyReason
      : studio.about?.hostsSessions === false
        ? 'Only the daemon hosts sessions · $ loadout dashboard --open'
        : studio.about?.startsSessions === false
          ? `This machine cannot host a terminal (${studio.about.pseudoTerminal ?? 'no pseudo-terminal'})`
          : undefined,
  );
</script>

<div class="head">
  <div>
    <h1>Sessions</h1>
    <span class="fact">{live.length} running</span>
  </div>
  <span class="grow"></span>
  <HaloButton label="New session…" cmd="opens the launch sheet · changes nothing yet" onpress={onlaunch} disabledReason={unable} />
</div>

{#if lost.length}
  <section class="lostbanner" aria-labelledby="z-lost">
    <h2 class="ls-zone" id="z-lost">Ended when the daemon stopped</h2>
    <p class="calm small">These were running here when the daemon last stopped. Nothing restarts by itself: each one starts an agent.</p>
    <ul class="slist" aria-label="Ended when the daemon stopped">
      {#each lost as one (one.id)}
        <li class="lostrow">
          <span class="t">{one.conversation!.title}</span>
          <span class="x">{one.conversation!.project ?? one.conversation!.directory} · {one.conversation!.agent}</span>
          <span class="acts">
            <HaloButton label="Resume" size="small" cmd="carries it on in a new terminal" disabledReason={unable ?? (busy ? 'one at a time' : undefined)} onpress={() => void resumeLost(one)} />
            <button type="button" class="linkish" disabled={studio.watchOnly} onclick={() => void studio.forgetLost(one)}>Not this one</button>
          </span>
        </li>
      {/each}
    </ul>
    {#if lost.length > 1}
      <HaloButton label={`Resume all ${lost.length}`} cmd="asks first, naming each agent" disabledReason={unable ?? (busy ? 'one at a time' : undefined)} onpress={() => (resumingAll = true)} />
    {/if}
  </section>
{/if}

<div class="two">
  <section aria-labelledby="z-live">
    <h2 class="ls-zone" id="z-live">Running and waiting</h2>
    {#if live.length}
      <ul class="slist" aria-label="Running and waiting">
        {#each live as line (line.id)}
          <li>
            <a class="srow" href={`#session/${encodeURIComponent(line.ref)}`}><span class="t">{line.title}</span><Signal word={wordFor(line, studio.idleAfter)} /><span class="x">{line.where} · {line.session?.agent ?? 'agent'} · {line.session?.mode ?? 'mode by the task'} · {line.mins ?? 0} min</span></a>
          </li>
        {/each}
      </ul>
    {:else}
      <p class="calm">No session is running here.</p>
    {/if}
    <p class="calm small">These run in the daemon, not in this tab: closing it ends nothing. Restarting the daemon ends every one of them.</p>
  </section>
  <section aria-labelledby="z-rec">
    <h2 class="ls-zone" id="z-rec">Recent</h2>
    {#if ended.length}
      <ul class="slist" aria-label="Recent sessions">
        {#each ended as line (line.id)}
          <li>
            <a class="srow" href={`#session/${encodeURIComponent(line.ref)}`}><span class="t">{line.title}</span><Signal word={wordFor(line, studio.idleAfter)} /><span class="x">{line.where} · {line.session?.agent ?? 'agent'} · ended{line.session?.exitCode === null || line.session?.exitCode === undefined ? '' : ` · exit ${line.session.exitCode}`}</span></a>
          </li>
        {/each}
      </ul>
    {:else}
      <p class="calm">Nothing has ended since the daemon started.</p>
    {/if}
    <Command why="Reopen the last conversation in a terminal:" command={command.resumeLast()} />
  </section>
</div>

<section class="history" aria-labelledby="z-hist">
  <div class="hhead">
    <h2 class="ls-zone" id="z-hist">History</h2>
    <span class="grow"></span>
    <label class="filter">Agent
      <select bind:value={agent}>
        <option value="">every agent</option>
        {#each agents as one (one)}<option value={one}>{one}</option>{/each}
      </select>
    </label>
    <label class="filter">Project
      <select bind:value={project}>
        <option value="">every project</option>
        {#each projects as one (one)}<option value={one}>{one}</option>{/each}
      </select>
    </label>
  </div>
  <p class="calm small">Every conversation from both agents, newest first. What it was launched for carries over only where its launch is on record; older ones carry nothing.</p>
  <ConversationList conversations={shownHistory} label="Past conversations" onchanges={onresume} />
</section>

{#if resumingAll}
  <Dialog
    title={`Resume all ${lost.length}?`}
    why={resumeAllWords(lost)}
    actions={[{ label: `Resume ${lost.length}`, solid: true, cmd: 'one terminal each', onpress: () => void resumeAll() }]}
    onclose={() => (resumingAll = false)}
  />
{/if}

<style>
  .lostbanner { margin: 0 0 18px; padding: 12px 14px; border: 1px solid var(--rule); border-left: 3px solid var(--accent); }
  .lostrow { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 2px 14px; align-items: center; padding: 6px 0; }
  .lostrow .t { font-size: 15px; color: var(--ink-strong); overflow-wrap: anywhere; }
  .lostrow .x { grid-column: 1; font-family: var(--font-mono); font-size: 11.5px; color: var(--muted); }
  .lostrow .acts { grid-row: 1 / span 2; grid-column: 2; display: grid; justify-items: end; gap: 4px; }
  .history { margin-top: 22px; }
  .hhead { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 14px; }
  .hhead .grow { flex: 1 1 auto; }
  .filter { display: inline-flex; gap: 6px; align-items: center; font-size: 12.5px; color: var(--muted); }
  .filter select { font: inherit; color: var(--ink); background: var(--surface, transparent); border: 1px solid var(--rule); padding: 3px 6px; }
  .linkish { border: 0; background: none; padding: 0; color: var(--accent); font: inherit; font-size: 12.5px; cursor: pointer; text-decoration: underline; text-underline-offset: 3px; }
  .linkish:disabled { color: var(--muted); cursor: default; text-decoration: none; }
  @media (max-width: 600px) {
    .lostrow { grid-template-columns: 1fr; }
    .lostrow .acts { grid-row: auto; grid-column: 1; justify-items: start; }
  }
</style>
