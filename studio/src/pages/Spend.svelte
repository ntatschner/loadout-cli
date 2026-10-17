<script lang="ts">
  // What the agents have spent, in tokens, and where: by project, day, model
  // or agent, with how much the cache saved. Below it, what is running now,
  // the recent sessions and launches, and what launched agents reported
  // about themselves. Read with the commands a terminal uses.
  import { untrack } from 'svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import { readJson, SignedOut } from '../lib/api';
  import { lineFor } from '../lib/commandLine';
  import { studio } from '../lib/state.svelte';

  interface Totals {
    input: number;
    output: number;
    thinking: number;
    cacheRead: number;
    total: number;
    billedInputEquivalent: number;
    savedFraction: number;
    cacheHitFraction: number;
  }

  interface Usage {
    since: string;
    complete: boolean;
    totals: Totals;
    groupedBy: string;
    groups: { name: string; registered?: boolean; totals: Totals }[];
  }

  interface Launch {
    id: string;
    startedAt: string;
    projectName: string;
    agent: string;
    mode: string;
    task: string | null;
    estimatedTokens: number;
    tokenBudget: number;
  }

  interface Running {
    launchId: string;
    project: string;
    agent: string;
    elapsedSeconds: number;
    state: string;
    headless: boolean;
  }

  interface Session {
    agent: string;
    id: string;
    title: string | null;
    directory: string;
    branch: string | null;
    lastActive: string;
  }

  const BY = ['project', 'day', 'model', 'agent'];

  let by = $state('project');
  let days = $state('30');
  let usage = $state<Usage | null>(null);
  let launches = $state<Launch[] | null>(null);
  let running = $state<Running[] | null>(null);
  let sessions = $state<Session[] | null>(null);
  let telemetry = $state<{ enabled: boolean; endpoint: string; sessions: number; tokens: number; listRateCostUsd: number } | null>(null);
  let status = $state<{ workspace: { configured: boolean; cloned: boolean }; projects: { registered: number; availableLocally: number; remoteOnly: number }; agents: { name: string; installed: boolean; version?: string }[]; defaultAgent: string } | null>(null);
  let problem = $state<string | null>(null);

  const usageWords = $derived(['--by', by, '--days', days]);

  function fail(error: unknown) {
    if (error instanceof SignedOut) {
      studio.signedOut = true;
    } else {
      problem = (error as Error).message;
    }
  }

  async function loadUsage() {
    try {
      usage = await readJson<Usage>('usage', usageWords);
    } catch (error) {
      fail(error);
    }
  }

  async function loadRest() {
    // Each is worth having without the others, so one failing does not hide the rest.
    readJson<Launch[]>('launches', ['--days', '7']).then((read) => (launches = read), fail);
    readJson<Running[]>('running').then((read) => (running = read), fail);
    readJson<Session[]>('sessions', ['--limit', '12']).then((read) => (sessions = read), fail);
    readJson<typeof telemetry & object>('telemetry status').then((read) => (telemetry = read), fail);
    readJson<typeof status & object>('status').then((read) => (status = read), fail);
  }

  $effect(() => {
    void loadUsage();
  });

  $effect(() => {
    untrack(() => void loadRest());
  });

  const settled = $derived(studio.jobs.filter((job) => job.state !== 'running').length);
  let seen = untrack(() => settled);

  $effect(() => {
    if (settled === seen) {
      return;
    }

    seen = settled;
    untrack(() => {
      void loadUsage();
      void loadRest();
    });
  });

  /** Tokens as somebody reads them: 1.2M rather than 1,234,567. */
  function tokens(value: number): string {
    if (value >= 1e9) return `${(value / 1e9).toFixed(1)}B`;
    if (value >= 1e6) return `${(value / 1e6).toFixed(1)}M`;
    if (value >= 1e3) return `${(value / 1e3).toFixed(1)}k`;

    return String(value);
  }

  const percent = (fraction: number) => `${Math.round(fraction * 100)}%`;
  const largest = $derived(Math.max(1, ...(usage?.groups.map((group) => group.totals.billedInputEquivalent) ?? [1])));
  const when = (at: string) => new Date(at).toLocaleString([], { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
  const minutes = (seconds: number) => (seconds < 120 ? `${seconds}s` : `${Math.round(seconds / 60)} min`);
</script>

<div class="head"><div><h1>Spend</h1><span class="fact">what the agents have used, in tokens, and where</span></div></div>

<div class="spend">
  {#if problem}<p class="offline" role="alert">{problem}</p>{/if}

  <section aria-labelledby="sp-usage">
    <h2 class="ls-zone" id="sp-usage">Tokens</h2>
    <form class="cmdform row" onsubmit={(event) => { event.preventDefault(); void loadUsage(); }}>
      <label class="fld"><span class="lab">By</span>
        <select name="spend-by" bind:value={by} onchange={() => void loadUsage()}>{#each BY as one (one)}<option value={one}>{one}</option>{/each}</select>
      </label>
      <label class="fld"><span class="lab">Over the last</span>
        <select name="spend-days" bind:value={days} onchange={() => void loadUsage()}>
          <option value="1">day</option><option value="7">week</option><option value="30">30 days</option><option value="90">90 days</option>
        </select>
      </label>
      <HaloButton label="Read it again" size="small" cmd={lineFor('usage', usageWords)} onpress={() => void loadUsage()} />
    </form>

    {#if usage}
      <dl class="facts">
        <dt>Billed as input</dt><dd><strong>{tokens(usage.totals.billedInputEquivalent)}</strong> tokens, after the cache saved {percent(usage.totals.savedFraction)}</dd>
        <dt>Written</dt><dd>{tokens(usage.totals.output)} output · {tokens(usage.totals.thinking)} thinking</dd>
        <dt>Read from the cache</dt><dd>{tokens(usage.totals.cacheRead)} · {percent(usage.totals.cacheHitFraction)} of input</dd>
        <dt>Since</dt><dd>{usage.since}{usage.complete ? '' : ' · some transcripts could not be read'}</dd>
      </dl>
      <ul class="bars" aria-label={`Billed input by ${usage.groupedBy}`}>
        {#each usage.groups.slice(0, 20) as group (group.name)}
          <li>
            <span class="n">{group.name}{group.registered === false ? ' · not a project' : ''}</span>
            <span class="bar" aria-hidden="true"><span class="fill" style:width={`${(group.totals.billedInputEquivalent / largest) * 100}%`}></span></span>
            <span class="v">{tokens(group.totals.billedInputEquivalent)}</span>
          </li>
        {/each}
      </ul>
      {#if usage.groups.length > 20}<p class="calm small">The {usage.groups.length - 20} smallest are left out here.</p>{/if}
    {:else}
      <p class="calm" role="status">Reading the transcripts…</p>
    {/if}
  </section>

  <section aria-labelledby="sp-now">
    <h2 class="ls-zone" id="sp-now">Running now</h2>
    {#if running?.length}
      <ul class="plain">{#each running as one (one.launchId)}<li><span class="n">{one.project}</span> · {one.agent} · {minutes(one.elapsedSeconds)} · {one.state}{one.headless ? ' · headless' : ''}</li>{/each}</ul>
    {:else if running}
      <p class="calm">No sessions are running.</p>
    {/if}
    <p class="calm small">From <code class="ls-cmd">loadout running</code>.</p>
  </section>

  <section aria-labelledby="sp-launches">
    <h2 class="ls-zone" id="sp-launches">Launched this week</h2>
    {#if launches?.length}
      <ul class="plain">
        {#each launches as one (one.id)}
          <li><span class="n">{one.projectName}</span> · {one.agent} · {one.mode}{one.task ? ` · ${one.task}` : ''} · {when(one.startedAt)} · started with {tokens(one.estimatedTokens)} of {tokens(one.tokenBudget)} tokens</li>
        {/each}
      </ul>
    {:else if launches}
      <p class="calm">Nothing launched this week.</p>
    {/if}
    <p class="calm small">From <code class="ls-cmd">loadout launches --days 7</code>.</p>
  </section>

  <section aria-labelledby="sp-sessions">
    <h2 class="ls-zone" id="sp-sessions">Recent sessions</h2>
    {#if sessions?.length}
      <ul class="plain">
        {#each sessions as one (one.id)}
          <li><span class="n">{one.title || one.id}</span> · {one.agent} · <code class="ls-cmd">{one.directory}</code>{one.branch ? ` · ${one.branch}` : ''} · {when(one.lastActive)}</li>
        {/each}
      </ul>
    {:else if sessions}
      <p class="calm">No sessions found.</p>
    {/if}
    <p class="calm small">From <code class="ls-cmd">loadout sessions --limit 12</code>.</p>
    <div class="acts">
      <CommandAction path="resume" title="Resume a session in a terminal here" />
    </div>
  </section>

  <section aria-labelledby="sp-reported">
    <h2 class="ls-zone" id="sp-reported">What agents reported</h2>
    {#if telemetry}
      <p class="calm">{telemetry.enabled ? `Collecting at ${telemetry.endpoint}: ${telemetry.sessions} sessions, ${tokens(telemetry.tokens)} tokens, $${telemetry.listRateCostUsd.toFixed(2)} at list rates.` : 'Not collecting. Launched agents report their own usage only while the receiver runs, which is loadout telemetry serve in a terminal.'}</p>
    {/if}
    <p class="calm small">From <code class="ls-cmd">loadout telemetry status</code>.</p>
  </section>

  <section aria-labelledby="sp-machine">
    <h2 class="ls-zone" id="sp-machine">This machine</h2>
    {#if status}
      <dl class="facts">
        <dt>Projects</dt><dd>{status.projects.registered} registered · {status.projects.availableLocally} here{status.projects.remoteOnly ? ` · ${status.projects.remoteOnly} elsewhere only` : ''}</dd>
        <dt>Workspace</dt><dd>{status.workspace.configured ? (status.workspace.cloned ? 'set up and cloned' : 'set up, not cloned') : 'not set up'}</dd>
        <dt>Agents</dt><dd>{status.agents.map((one) => `${one.name}${one.installed ? (one.version ? ` ${one.version}` : '') : ' (not installed)'}`).join(' · ')} · {status.defaultAgent} by default</dd>
      </dl>
      <p class="calm small">From <code class="ls-cmd">loadout status</code>.</p>
    {/if}
  </section>

  <div class="acts">
    <CommandAction path="spend refresh" title="Work out where spending stands for the status line" label="Refresh it" />
    <CommandAction path="usage" title="Usage your own way: by model, a project, or as a table" label="Show" />
  </div>
</div>
