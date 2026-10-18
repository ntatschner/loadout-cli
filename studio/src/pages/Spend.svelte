<script lang="ts">
  // Spend: one number, billed as input, and what the cache saved; where it
  // went, by project, day, model or agent; then what is running now, what
  // was launched, the recent sessions, what agents reported about
  // themselves and this machine. Read with the commands a terminal uses.
  // There are no budgets here: thresholds belong to team runs.
  import { untrack } from 'svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import Signal from '../lib/components/Signal.svelte';
  import SpendBars from '../lib/components/SpendBars.svelte';
  import TerminalOnly from '../lib/components/TerminalOnly.svelte';
  import TokenSplit from '../lib/components/TokenSplit.svelte';
  import { readJson, SignedOut } from '../lib/api';
  import { lineFor } from '../lib/commandLine';
  import { tokens } from '../lib/glyphs';
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

  /** period and grouping: set from outside, as a Pure Focus desk's actions do; the page's own controls change them too. */
  let { period = undefined, grouping = undefined }: { period?: string; grouping?: string } = $props();

  const BY = ['project', 'day', 'model', 'agent'];
  const PERIODS = [
    ['1', 'day'],
    ['7', 'week'],
    ['30', '30 days'],
    ['90', '90 days'],
  ] as const;

  let by = $state(untrack(() => grouping) ?? 'project');
  let days = $state(untrack(() => period) ?? '30');

  $effect(() => {
    if (period) {
      days = period;
    }
  });

  $effect(() => {
    if (grouping) {
      by = grouping;
    }
  });
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

  async function loadUsage(words: string[]) {
    try {
      const read = await readJson<Usage>('usage', words);

      usage = read;

      // The rail says what the last 30 days cost, from this read when it is that one.
      if (words[3] === '30') {
        studio.spentMonth = read.totals.billedInputEquivalent;
      }
    } catch (error) {
      fail(error);
    }
  }

  function loadRest() {
    // Each is worth having without the others, so one failing does not hide the rest.
    readJson<Launch[]>('launches', ['--days', '7']).then((read) => (launches = read), fail);
    readJson<Running[]>('running').then((read) => (running = read), fail);
    readJson<Session[]>('sessions', ['--limit', '12']).then((read) => (sessions = read), fail);
    readJson<NonNullable<typeof telemetry>>('telemetry status').then((read) => (telemetry = read), fail);
    readJson<NonNullable<typeof status>>('status').then((read) => (status = read), fail);
  }

  $effect(() => {
    const words = usageWords;

    untrack(() => void loadUsage(words));
  });

  $effect(() => {
    untrack(() => loadRest());
  });

  const settled = $derived(studio.jobs.filter((job) => job.state !== 'running').length);
  let seen = untrack(() => settled);

  $effect(() => {
    if (settled === seen) {
      return;
    }

    seen = settled;
    untrack(() => {
      void loadUsage(usageWords);
      loadRest();
    });
  });

  // Arrow keys move along the period control, as a radio group does.
  function periodKeys(event: KeyboardEvent) {
    const at = PERIODS.findIndex(([value]) => value === days);
    const to = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? at + 1 : event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? at - 1 : null;

    if (to !== null) {
      event.preventDefault();
      days = PERIODS[(to + PERIODS.length) % PERIODS.length][0];
      (event.currentTarget as HTMLElement).querySelector<HTMLElement>(`[data-days="${days}"]`)?.focus();
    }
  }

  const bars = $derived((usage?.groups ?? []).map((group) => ({ name: group.name, value: group.totals.billedInputEquivalent, registered: group.registered })));
  // By day the columns read oldest first.
  const ordered = $derived(by === 'day' ? [...bars].sort((a, b) => a.name.localeCompare(b.name)) : bars);
  const when = (at: string) => new Date(at).toLocaleString([], { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
  const minutes = (seconds: number) => (seconds < 120 ? `${seconds}s` : `${Math.round(seconds / 60)} min`);
</script>

<div class="head">
  <div><h1>Spend</h1><span class="fact">what the agents have used, in tokens, and where</span></div>
  <span class="grow"></span>
  <div class="spctl">
    <span class="lab" id="sp-period">Over the last</span>
    <!-- svelte-ignore a11y_interactive_supports_focus -->
    <div class="periodseg" role="radiogroup" aria-labelledby="sp-period" onkeydown={periodKeys}>
      {#each PERIODS as [value, label] (value)}
        <button type="button" role="radio" data-days={value} aria-checked={days === value ? 'true' : 'false'} tabindex={days === value ? 0 : -1} onclick={() => (days = value)}>{label}</button>
      {/each}
    </div>
  </div>
</div>

{#if problem}<p class="offline" role="alert">{problem}</p>{/if}

<section class="pblock hero" aria-labelledby="sp-usage">
  <h2 class="ls-zone" id="sp-usage">Tokens</h2>
  {#if usage}
    <TokenSplit totals={usage.totals} since={usage.since} complete={usage.complete} from={lineFor('usage', usageWords)} />
  {:else}
    <p class="calm" role="status">Reading the transcripts…</p>
  {/if}
</section>

<div class="spgrid">
  <section class="pblock wide" aria-labelledby="sp-where">
    <h2 class="ls-zone" id="sp-where">Where it went</h2>
    <div class="psel inline">
      <label for="spend-by">By</label>
      <select id="spend-by" class="ls-text" bind:value={by}>{#each BY as one (one)}<option value={one}>{one}</option>{/each}</select>
    </div>
    {#if usage}
      {#key `${by}/${days}`}
        <SpendBars groups={ordered} {by} label={`Tokens billed as input by ${by}`} />
      {/key}
    {/if}
  </section>

  <section class="pblock" aria-labelledby="sp-now">
    <h2 class="ls-zone" id="sp-now">Running now</h2>
    {#if running?.length}
      <ul class="rowsx">{#each running as one (one.launchId)}<li><Signal word={one.state === 'working' ? 'working' : one.state} /><b>{one.project}</b><span>{one.agent} · {minutes(one.elapsedSeconds)}{one.headless ? ' · headless' : ''}</span></li>{/each}</ul>
    {:else if running}
      <p class="lead">No sessions are running.</p>
    {/if}
    <p class="ls-from">From <code>$ loadout running</code>.</p>
  </section>
</div>

<div class="spgrid">
  <section class="pblock wide" aria-labelledby="sp-launches">
    <h2 class="ls-zone" id="sp-launches">Launched this week</h2>
    {#if launches?.length}
      <ul class="rowsx">
        {#each launches as one (one.id)}
          <li><b>{one.projectName}</b><span>{one.agent} · {one.mode}{one.task ? ` · ${one.task}` : ''}</span><span>{when(one.startedAt)}</span><code>started with {tokens(one.estimatedTokens)} of {tokens(one.tokenBudget)}</code></li>
        {/each}
      </ul>
    {:else if launches}
      <p class="lead">Nothing launched this week.</p>
    {/if}
    <p class="ls-from">From <code>$ loadout launches --days 7</code>.</p>

    <h2 class="ls-zone sub-zone" id="sp-sessions">Recent sessions</h2>
    {#if sessions?.length}
      <ul class="rowsx">
        <!-- Keyed by agent, folder and id together: two agents' sessions can share an id, and one clash stops the whole list drawing. -->
        {#each sessions as one (`${one.agent}:${one.directory}:${one.id}`)}
          <li><b>{one.title || one.id}</b><span>{one.agent}</span><code>{one.directory}</code>{#if one.branch}<span>{one.branch}</span>{/if}<span>{when(one.lastActive)}</span></li>
        {/each}
      </ul>
    {:else if sessions}
      <p class="lead">No sessions found.</p>
    {/if}
    <p class="ls-from">From <code>$ loadout sessions --limit 12</code>.</p>
    <div class="ls-drawers">
      <CommandAction path="resume" title="Resume a session in a terminal here" />
    </div>
  </section>

  <div>
    <section class="pblock" aria-labelledby="sp-reported">
      <h2 class="ls-zone" id="sp-reported">What agents reported</h2>
      {#if telemetry}
        <p class="lead">{telemetry.enabled ? `Collecting at ${telemetry.endpoint}: ${telemetry.sessions} sessions, ${tokens(telemetry.tokens)} tokens, $${telemetry.listRateCostUsd.toFixed(2)} at list rates.` : 'Not collecting. Launched agents report their own usage only while the receiver runs.'}</p>
      {/if}
      <p class="ls-from">From <code>$ loadout telemetry status</code>.</p>
      <TerminalOnly title="Run the receiver" command="$ loadout telemetry serve" why={studio.command('telemetry serve')?.reach.reason ? `It ${studio.command('telemetry serve')?.reach.reason}.` : undefined} />
    </section>

    <section class="pblock" aria-labelledby="sp-machine">
      <h2 class="ls-zone" id="sp-machine">This machine</h2>
      {#if status}
        <dl class="facts">
          <dt>Projects</dt><dd>{status.projects.registered} registered · {status.projects.availableLocally} here{status.projects.remoteOnly ? ` · ${status.projects.remoteOnly} elsewhere only` : ''}</dd>
          <dt>Workspace</dt><dd>{status.workspace.configured ? (status.workspace.cloned ? 'set up and cloned' : 'set up, not cloned') : 'not set up'}</dd>
          <dt>Agents</dt><dd>{status.agents.map((one) => `${one.name}${one.installed ? (one.version ? ` ${one.version}` : '') : ' (not installed)'}`).join(' · ')} · {status.defaultAgent} by default</dd>
        </dl>
        <p class="ls-from">From <code>$ loadout status</code>.</p>
      {/if}
      <div class="ls-drawers">
        <CommandAction path="usage" title="Usage your own way: by model, a project, or as a table" label="Show" />
        <CommandAction path="spend refresh" title="Work out where spending stands for the status line" label="Refresh it" />
      </div>
    </section>
  </div>
</div>
