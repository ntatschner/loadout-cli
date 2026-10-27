<script lang="ts">
  // A hosted session's status, one row on the foot of its terminal, from
  // what its status line last handed over: the model, the context, what it
  // has cost, the plan's five-hour window and how many specialists it was
  // given. Every figure is Claude's; one it did not send is left out rather
  // than drawn as nought. Old figures say so, since Claude only redraws on
  // its own events and its refresh interval.
  //
  // A screen reader hears it only when the context crosses a threshold, and
  // politely: the figures change every redraw, and reading each one out
  // would drown the terminal.
  import { untrack } from 'svelte';
  import {
    ago,
    contextPercent,
    dollars,
    isStale,
    levelOf,
    levelWords,
    resetsIn,
    type Level,
    type SessionStatus,
  } from '../status';

  let { status, now }: { status: SessionStatus; now: number } = $props();

  const percent = $derived(contextPercent(status));
  const level = $derived(levelOf(percent));
  const cost = $derived(dollars(status.cost?.usd));
  const five = $derived(status.limits?.fiveHour ?? null);
  const fiveLevel = $derived(levelOf(five?.usedPercent));
  const spend = $derived(status.limits?.spend ?? null);
  const stale = $derived(isStale(status, now));
  const count = $derived(status.specialists?.length ?? 0);

  // Said once each time the context moves from one level to another, never
  // on the first reading: a page opened on a full session shows it.
  let said = $state('');
  let last: Level | null = null;

  $effect(() => {
    const current = level;
    const before = untrack(() => last);

    last = current;

    if (before !== null && before !== current) {
      said = current === 'ok'
        ? `Context back to ${untrack(() => percent)}%`
        : `Context ${untrack(() => percent)}% used, ${levelWords(current)}`;
    }
  });
</script>

<div class="ls-sstrip" class:stale role="group" aria-label="Session status">
  {#if status.model}<span class="model">{status.model}</span>{/if}
  {#if percent !== null}
    <span class={`gauge ${level}`}>
      <span class="k">ctx</span>
      <i class="beam" aria-hidden="true"><i style:width={`${Math.min(100, percent)}%`}></i></i>
      <b>{percent}%</b>
      {#if level !== 'ok'}<em>{levelWords(level)}</em>{/if}
    </span>
  {/if}
  {#if cost}<span class="cost"><span class="k">cost</span> <b>{cost}</b></span>{/if}
  {#if five}
    <span class={`gauge ${fiveLevel}`} title={resetsIn(five.resetsAt, now) ?? undefined}>
      <span class="k">5h</span>
      <i class="beam" aria-hidden="true"><i style:width={`${Math.min(100, five.usedPercent)}%`}></i></i>
      <b>{Math.round(five.usedPercent)}%</b>
    </span>
  {:else if spend?.usedPercent !== null && spend?.usedPercent !== undefined}
    <span class={`gauge ${levelOf(spend.usedPercent)}`}>
      <span class="k">spend</span>
      <i class="beam" aria-hidden="true"><i style:width={`${Math.min(100, spend.usedPercent)}%`}></i></i>
      <b>{Math.round(spend.usedPercent)}%</b>
    </span>
  {/if}
  {#if count}<span class="spec"><b>{count}</b> <span class="k">{count === 1 ? 'specialist' : 'specialists'}</span></span>{/if}
  <span class="grow"></span>
  <span class="when">{stale ? `old: ${ago(status.at, now)}` : ago(status.at, now)}</span>
  <span class="ls-vh" role="status" aria-live="polite">{said}</span>
</div>
