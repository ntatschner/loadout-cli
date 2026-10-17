<script lang="ts">
  // Every command Loadout has, each with a form built from its own options,
  // the line it will run shown as it is filled in, and what it said. The
  // pages for each area do these better; this is how anything a page does
  // not do yet is still within reach, and what is left to the terminal says
  // why.
  import CommandForm from '../lib/components/CommandForm.svelte';
  import JobResult from '../lib/components/JobResult.svelte';
  import { commands, SignedOut, type Job, type StudioCommand } from '../lib/api';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';

  let { rest, onlaunch }: { rest: string[]; onlaunch: () => void } = $props();

  let find = $state('');
  let job = $state<Job | null>(null);
  let problem = $state<string | null>(null);

  const jobId = $derived(rest[0] === 'run' ? (rest[1] ?? null) : null);
  const path = $derived(rest[0] === 'run' ? (job?.path ?? '') : rest.join(' '));
  const chosen = $derived(studio.command(path));

  $effect(() => {
    studio.loadCatalogue().catch((error) => {
      if (error instanceof SignedOut) {
        studio.signedOut = true;
      } else {
        problem = (error as Error).message;
      }
    });
  });

  // Which command a job ran, so its area in the list stays open.
  $effect(() => {
    if (jobId) {
      commands.job(jobId).then((read) => (job = read), () => (job = null));
    } else {
      job = null;
    }
  });

  const shown = $derived.by(() => {
    const words = find.trim().toLowerCase().split(/\s+/).filter(Boolean);
    const matching = studio.catalogue.filter((one) => words.every((word) => `${one.path} ${one.description}`.toLowerCase().includes(word)));
    const groups = new Map<string, StudioCommand[]>();

    for (const one of matching) {
      const name = one.category || 'Other';
      groups.set(name, [...(groups.get(name) ?? []), one]);
    }

    return [...groups.entries()];
  });

  const stateWord = (state: string) => (state === 'cancelled' ? 'stopped' : state);
  const when = (at: string) => new Date(at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
</script>

<div class="head"><div><h1>Commands</h1><span class="fact">everything Loadout can do, each with the line it runs</span></div></div>

<div class="memory">
  <aside class="memside" aria-label="Commands">
    <label class="find"><span class="ls-sr">Find a command</span><input type="search" name="find" placeholder="Find a command" bind:value={find} autocomplete="off" /></label>

    {#if studio.jobs.length}
      <section class="store" aria-labelledby="cm-recent">
        <h2 class="ls-zone" id="cm-recent">Run from here</h2>
        <ul class="slist">
          {#each studio.jobs.slice(0, 8) as one (one.id)}
            <li>
              <a class="srow" href={`#commands/run/${one.id}`} aria-current={jobId === one.id ? 'page' : undefined}>
                <span class="t">{one.path}</span><span class="x">{stateWord(one.state)} · {when(one.startedAt)}</span>
              </a>
            </li>
          {/each}
        </ul>
      </section>
    {/if}

    <!-- Folded by area, since two hundred commands at once is a wall: the
         chosen command's area is open, and every area opens while finding. -->
    {#each shown as [category, list] (category)}
      <details class="store fold" open={find.trim().length > 0 || list.some((one) => one.path === path)}>
        <summary><h2 class="ls-zone">{category} <span class="count">{list.length}</span></h2></summary>
        <ul class="slist">
          {#each list as one (one.path)}
            <li>
              <a class="srow" href={`#commands/${one.path.split(' ').map(encodeURIComponent).join('/')}`} aria-current={!jobId && path === one.path ? 'page' : undefined}>
                <span class="t">{one.path}</span><span class="x">{one.description}</span>
              </a>
            </li>
          {/each}
        </ul>
      </details>
    {:else}
      {#if studio.catalogue.length}<p class="calm">Nothing matches.</p>{:else}<p class="calm" role="status">Reading…</p>{/if}
    {/each}
  </aside>

  <section class="memmain" aria-label="Command">
    {#if problem}<p class="offline" role="alert">{problem}</p>{/if}

    {#if jobId}
      {#key jobId}
        <JobResult id={jobId} onagain={(ran) => router.go('commands', ...ran.path.split(' '))} />
      {/key}
    {:else if chosen}
      <h2 class="topicname">loadout {chosen.path}</h2>
      {#key chosen.path}
        <CommandForm command={chosen} {onlaunch} onstarted={(started) => router.go('commands', 'run', started.id)} />
      {/key}
    {:else if studio.catalogue.length}
      <p class="calm">Choose a command to see what it takes and run it. Each area's own page does these better as it is built; this reaches everything in the meantime.</p>
    {/if}
  </section>
</div>
