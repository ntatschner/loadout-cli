<script lang="ts">
  // Sessions: what the daemon is hosting, running and waiting first, then
  // the ones that have ended. Every one is the daemon's, so closing the tab
  // ends nothing; a daemon restart ends them all, which the page says.
  import HaloButton from '../lib/components/HaloButton.svelte';
  import Signal from '../lib/components/Signal.svelte';
  import Command from '../lib/components/Command.svelte';
  import { wordFor } from '../lib/signals';
  import { studio } from '../lib/state.svelte';
  import { command } from '../lib/commands';

  let { onlaunch }: { onlaunch: () => void } = $props();

  const all = $derived(studio.lines.filter((line) => line.kind === 'session'));
  const live = $derived(all.filter((line) => line.stage !== 4));
  const ended = $derived(all.filter((line) => line.stage === 4));

  const unable = $derived(
    studio.watchOnly
      ? studio.watchOnlyReason
      : studio.about?.hostsSessions === false
        ? 'Only the daemon hosts sessions · $ loadout dashboard --open --studio'
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
