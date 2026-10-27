<script lang="ts">
  // A hosted session's status in full, for its Details: the context window
  // and what fills it, what the session has cost, each of the plan's usage
  // windows with when it resets, and every specialist it was given, by name
  // and linked to what it says. Its only action is a command, shown whole,
  // for bringing a line back to the terminal: nothing here types into the
  // session.
  import Command from './Command.svelte';
  import Meter from './Meter.svelte';
  import {
    ago,
    CONTEXT_URGENT,
    CONTEXT_WARN,
    dollars,
    isStale,
    resetsIn,
    tokens,
    type SessionStatus,
    type StatusWindow,
  } from '../status';

  let { status, now }: { status: SessionStatus; now: number } = $props();

  const context = $derived(status.context);
  const percent = $derived(context?.usedPercent ?? null);
  const limits = $derived(status.limits);
  const stale = $derived(isStale(status, now));

  const windows = $derived(
    [
      ['Five-hour window', limits?.fiveHour],
      ['Seven-day window', limits?.sevenDay],
    ].filter((one): one is [string, StatusWindow] => !!one[1]),
  );

  const specialistHref = (id: string) =>
    `#context/${encodeURIComponent(status.project ?? '')}/specialist/${encodeURIComponent(id)}`;

  const terminalWords = $derived(
    status.terminal === 'full'
      ? status.wrapping ? 'your own status line, run for you' : 'the launcher’s full line'
      : status.terminal === 'minimal' ? 'a short line' : 'no line: Studio draws it here',
  );
</script>

<section class="ls-sblock" aria-label="Status">
  <h2>Status</h2>
  <p class="when" class:stale>
    {stale ? `These figures are ${ago(status.at, now).replace(' ago', '')} old: the session has not redrawn its line since.` : `Updated ${ago(status.at, now)}.`}
  </p>

  {#if percent !== null}
    <Meter
      label="Context window used"
      value={percent}
      max={100}
      threshold={CONTEXT_WARN}
      thresholdLabel={`${CONTEXT_WARN}%`}
      overWords={percent >= CONTEXT_URGENT ? 'nearly full' : 'filling'}
      format={(value) => `${Math.round(value)}%`}
      of={() => (context?.windowSize ? `of ${tokens(context.windowSize)} tokens` : '')}
    />
  {/if}
  {#if context}
    <dl>
      {#if context.inputTokens !== null}<dt>In the window</dt><dd>{tokens(context.inputTokens)} tokens</dd>{/if}
      {#if context.cacheReadTokens !== null}<dt>Read from cache</dt><dd>{tokens(context.cacheReadTokens)}</dd>{/if}
      {#if context.cacheWriteTokens !== null}<dt>Written to cache</dt><dd>{tokens(context.cacheWriteTokens)}</dd>{/if}
      {#if context.uncachedTokens !== null}<dt>Not cached</dt><dd>{tokens(context.uncachedTokens)}</dd>{/if}
      {#if context.outputTokens !== null}<dt>Last reply</dt><dd>{tokens(context.outputTokens)}</dd>{/if}
    </dl>
  {/if}

  {#if status.cost}
    <dl>
      {#if dollars(status.cost.usd)}<dt>Cost so far</dt><dd>{dollars(status.cost.usd)} <span class="src">Claude’s estimate</span></dd>{/if}
      {#if status.cost.linesAdded !== null || status.cost.linesRemoved !== null}
        <dt>Lines</dt><dd>+{status.cost.linesAdded ?? 0} −{status.cost.linesRemoved ?? 0}</dd>
      {/if}
    </dl>
  {/if}

  {#each windows as [name, window] (name)}
    <Meter
      label={name}
      value={window.usedPercent}
      max={100}
      threshold={CONTEXT_WARN}
      thresholdLabel={`${CONTEXT_WARN}%`}
      overWords="getting close"
      format={(value) => `${Math.round(value)}%`}
      of={() => resetsIn(window.resetsAt, now) ?? ''}
    />
  {/each}
  {#if limits?.spend}
    {@const spend = limits.spend}
    <dl>
      <dt>Spend limit</dt>
      <dd>
        {#if spend.usedUsd !== null && spend.limitUsd !== null}{dollars(spend.usedUsd)} of {dollars(spend.limitUsd)}{:else if spend.usedPercent !== null}{Math.round(spend.usedPercent)}%{/if}
        {#if spend.period} {spend.period}{/if}
        {#if resetsIn(spend.resetsAt, now)} · {resetsIn(spend.resetsAt, now)}{/if}
      </dd>
    </dl>
  {/if}
  {#if !limits}
    <p class="calm small">No usage limits: Claude sends them for a subscription, or behind a gateway with a spend limit.</p>
  {/if}

  {#if status.specialists?.length}
    <h3>Specialists</h3>
    <ul class="specs">
      {#each status.specialists as id (id)}
        <li>{#if status.project}<a href={specialistHref(id)}>{id}</a>{:else}{id}{/if}</li>
      {/each}
    </ul>
  {/if}

  <p class="calm small">The terminal shows {terminalWords}.</p>
  <Command
    why="Change what the terminal shows:"
    command={`loadout config set statusline-in-studio ${status.terminal === 'off' ? 'full' : 'off'}`}
  />
</section>
