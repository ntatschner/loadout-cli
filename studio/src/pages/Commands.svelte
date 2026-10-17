<script lang="ts">
  // Every command Loadout has, each with a form built from its own options,
  // the line it will run shown as it is filled in, and what it said. The
  // pages for each area do these better; this is how anything a page does
  // not do yet is still within reach, and what is left to the terminal says
  // why.
  import Command from '../lib/components/Command.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import Signal from '../lib/components/Signal.svelte';
  import { commands, SignedOut, type Job, type StudioCommand, type StudioOption } from '../lib/api';
  import { lineFor, problemWith, wordsFor } from '../lib/commandLine';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';

  let { rest, onlaunch }: { rest: string[]; onlaunch: () => void } = $props();

  let catalogue = $state<StudioCommand[]>([]);
  let find = $state('');
  // Text for arguments and valued options, true or false for flags.
  let values = $state<Record<string, any>>({});
  let job = $state<Job | null>(null);
  let problem = $state<string | null>(null);
  let starting = $state(false);

  const jobId = $derived(rest[0] === 'run' ? (rest[1] ?? null) : null);
  const path = $derived(rest[0] === 'run' ? (job?.path ?? '') : rest.join(' '));
  const chosen = $derived(catalogue.find((one) => one.path === path) ?? null);

  const EFFECT: Record<string, string> = {
    reads: 'reads only',
    changes: 'changes something',
    session: 'starts a session',
    terminalOnly: 'terminal only',
  };

  function fail(error: unknown) {
    if (error instanceof SignedOut) {
      studio.signedOut = true;
    } else {
      problem = (error as Error).message;
    }
  }

  $effect(() => {
    commands.catalogue().then((all) => (catalogue = all), fail);
  });

  // A new command starts with an empty form.
  $effect(() => {
    void path;
    values = {};
    problem = null;
  });

  // The job shown is read again whenever the list says something changed,
  // which is how its output arrives once it ends.
  $effect(() => {
    void studio.jobs;

    if (jobId) {
      commands.job(jobId).then((read) => (job = read), fail);
    } else {
      job = null;
    }
  });

  const shown = $derived.by(() => {
    const words = find.trim().toLowerCase().split(/\s+/).filter(Boolean);
    const matching = catalogue.filter((one) => words.every((word) => `${one.path} ${one.description}`.toLowerCase().includes(word)));
    const groups = new Map<string, StudioCommand[]>();

    for (const one of matching) {
      const name = one.category || 'Other';
      groups.set(name, [...(groups.get(name) ?? []), one]);
    }

    return [...groups.entries()];
  });

  const own = $derived(chosen?.options.filter((option) => !option.global) ?? []);
  const everyCommand = $derived(chosen?.options.filter((option) => option.global && option.name !== '--non-interactive') ?? []);
  const words = $derived(chosen ? wordsFor(chosen, values) : []);
  const notYet = $derived(chosen ? problemWith(chosen, values) : null);
  const changes = $derived(chosen?.reach.effect === 'changes');
  const previewable = $derived(changes && chosen?.options.some((option) => option.name === '--dry-run'));
  const runReason = $derived(
    notYet ?? (studio.watchOnly && chosen?.reach.effect !== 'reads' ? 'Studio is only watching: preview it, or type the command in a terminal' : undefined),
  );

  async function run(preview: boolean) {
    if (!chosen) {
      return;
    }

    starting = true;
    problem = null;

    try {
      const started = await commands.run(chosen.path, preview && !words.includes('--dry-run') ? [...words, '--dry-run'] : words);

      await studio.loadJobs();
      router.go('commands', 'run', started.id);
    } catch (error) {
      fail(error);
    } finally {
      starting = false;
    }
  }

  async function cancel() {
    if (job) {
      try {
        await commands.cancel(job.id);
      } catch (error) {
        fail(error);
      }
    }
  }

  /** The output as data when it is data, so it reads as a structure rather than one long line. */
  function pretty(output: string): string {
    try {
      return JSON.stringify(JSON.parse(output), null, 2);
    } catch {
      return output;
    }
  }

  const stateWord = (state: string) => (state === 'cancelled' ? 'stopped' : state);
  const when = (at: string) => new Date(at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  const seconds = (one: Job) => (one.endedAt ? Math.max(0, Math.round((Date.parse(one.endedAt) - Date.parse(one.startedAt)) / 1000)) : null);

  const id = (name: string) => `f-${name.replace(/[^a-z0-9]/gi, '-')}`;
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
      {#if catalogue.length}<p class="calm">Nothing matches.</p>{:else}<p class="calm" role="status">Reading…</p>{/if}
    {/each}
  </aside>

  <section class="memmain" aria-label="Command">
    {#if problem}<p class="offline" role="alert">{problem}</p>{/if}

    {#if jobId && job}
      <h2 class="topicname">{job.path}</h2>
      <div class="jobhead">
        <Signal word={stateWord(job.state)} />
        <span class="calm small">started {when(job.startedAt)}{#if seconds(job) !== null} · took {seconds(job)}s{/if}{#if job.exitCode !== null} · exit code {job.exitCode}{/if}</span>
      </div>
      <Command command={`$ ${job.line}`} />
      {#if job.state === 'running'}
        <div class="ls-answers flush">
          <HaloButton label="Cancel" kind="danger" size="small" cmd="stops it and anything it started" disabledReason={studio.watchOnly ? studio.watchOnlyReason : undefined} onpress={() => void cancel()} />
        </div>
        <p class="calm" role="status">Running. What it says appears here when it ends.</p>
      {:else}
        {#if job.output}
          <h3 class="ls-zone">What it said</h3>
          <!-- Focusable so a keyboard can scroll it: output wider than the page scrolls inside its box, not the page. -->
          <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
          <pre class="output" tabindex="0" aria-label="What it said">{pretty(job.output)}</pre>
        {:else}
          <p class="calm">It said nothing on standard output.</p>
        {/if}
        {#if job.error}
          <h3 class="ls-zone">On standard error</h3>
          <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
          <pre class="output err" tabindex="0" aria-label="On standard error">{job.error}</pre>
        {/if}
        {#if job.truncated}<p class="calm small">It said more than Studio keeps; the end was left out.</p>{/if}
        <div class="ls-answers flush">
          <HaloButton label="Run it again" size="small" cmd="opens its form" onpress={() => router.go('commands', ...job!.path.split(' '))} />
        </div>
      {/if}
    {:else if chosen}
      <h2 class="topicname">loadout {chosen.path}</h2>
      <p class="calm">{chosen.description} <span class="effect">· {EFFECT[chosen.reach.effect]}</span></p>

      {#if chosen.reach.effect === 'terminalOnly'}
        <Command why={`Studio does not run this: it ${chosen.reach.reason}.`} command={`$ loadout ${chosen.path}`} />
      {:else if chosen.reach.effect === 'session'}
        <p class="calm">This starts an agent session. Studio hosts sessions from Sessions, where you can type into them.</p>
        <div class="ls-answers flush">
          <HaloButton label="Start a session…" solid size="small" cmd="opens New session" disabledReason={studio.watchOnly ? studio.watchOnlyReason : undefined} onpress={onlaunch} />
        </div>
        <Command why="Or in a terminal:" command={`$ loadout ${chosen.path}`} />
      {:else if !chosen.runnable}
        <p class="calm">This groups the commands under it. Choose one of them.</p>
      {:else}
        <form class="cmdform" onsubmit={(event) => { event.preventDefault(); if (!runReason) void run(false); }}>
          {#each chosen.arguments as argument (argument.name)}
            <label class="fld" for={id(argument.name)}>
              <span class="lab">{argument.name.toLowerCase()}{argument.required ? '' : ' (optional)'}</span>
              {#if argument.many}
                <textarea id={id(argument.name)} rows="3" bind:value={values[argument.name]} spellcheck="false"></textarea>
                <span class="calm small">One on each line. {argument.description}</span>
              {:else}
                <input id={id(argument.name)} bind:value={values[argument.name]} autocomplete="off" spellcheck="false" />
                {#if argument.description}<span class="calm small">{argument.description}</span>{/if}
              {/if}
            </label>
          {/each}

          {#snippet option(one: StudioOption)}
            {#if one.kind === 'flag'}
              <label class="chk"><input type="checkbox" bind:checked={values[one.name]} /> <span><code class="ls-cmd">{one.name}</code> {one.description}</span></label>
            {:else if one.choices.length}
              <label class="fld" for={id(one.name)}>
                <span class="lab"><code class="ls-cmd">{one.name}</code>{one.required ? '' : ' (optional)'}</span>
                <select id={id(one.name)} bind:value={values[one.name]}>
                  <option value="">not given</option>
                  {#each one.choices as choice (choice)}<option value={choice}>{choice}</option>{/each}
                </select>
                <span class="calm small">{one.description}</span>
              </label>
            {:else}
              <label class="fld" for={id(one.name)}>
                <span class="lab"><code class="ls-cmd">{one.name}</code>{one.valueName ? ` ${one.valueName.toLowerCase()}` : ''}{one.required ? '' : ' (optional)'}</span>
                {#if one.kind === 'values'}
                  <textarea id={id(one.name)} rows="2" bind:value={values[one.name]} spellcheck="false"></textarea>
                  <span class="calm small">One on each line. {one.description}</span>
                {:else}
                  <input id={id(one.name)} bind:value={values[one.name]} autocomplete="off" spellcheck="false" />
                  <span class="calm small">{one.kind === 'optional' ? 'Leave it empty to give it with no value. ' : ''}{one.description}</span>
                {/if}
              </label>
            {/if}
          {/snippet}

          {#each own as one (one.name)}{@render option(one)}{/each}

          {#if everyCommand.length}
            <details class="more">
              <summary>Options every command has</summary>
              {#each everyCommand as one (one.name)}{@render option(one)}{/each}
            </details>
          {/if}

          <Command why="It runs:" command={lineFor(chosen.path, words)} />

          <div class="ls-answers flush">
            {#if previewable}
              <HaloButton label="Preview" size="small" cmd="adds --dry-run · changes nothing" disabledReason={notYet ?? undefined} onpress={() => void run(true)} />
            {/if}
            <HaloButton label={starting ? 'Starting…' : 'Run'} solid={!changes} kind={changes ? 'needs' : ''} size="small" cmd={changes ? 'changes something' : 'reads only'} disabledReason={runReason} onpress={() => void run(false)} />
          </div>
        </form>
      {/if}
    {:else if catalogue.length}
      <p class="calm">Choose a command to see what it takes and run it. Each area's own page does these better as it is built; this reaches everything in the meantime.</p>
    {/if}
  </section>
</div>
