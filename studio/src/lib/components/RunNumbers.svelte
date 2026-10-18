<script lang="ts">
  // The numbers that change a decision about a run: what it costs a round
  // and where that is projected to end, where the time went, and what went
  // wrong. Projected from rounds, not the clock.
  import Signal from './Signal.svelte';
  import { css } from '../css';
  import { money } from '../teams';

  interface Props {
    rate: string;
    spent: number;
    cap?: number | null;
    projected?: number | null;
    /** By when the projection runs: "its last round", "round 6". */
    by?: string | null;
    byRound?: [string, number][];
    byNode?: [string, number][];
    wrong?: [string, number][];
  }

  let { rate, spent, cap = null, projected = null, by = null, byRound = [], byNode = [], wrong = [] }: Props = $props();

  const over = $derived(!!(cap && projected && projected > cap));
  const projection = $derived(
    (projected ? `Projected ${money(projected)} by ${by ?? 'its last round'}` : '')
      + (cap ? (over ? `, past its ${money(cap)} budget.` : `, inside its ${money(cap)} budget.`) : '. It has no cap, on purpose.'),
  );
</script>

{#snippet bars(rows: [string, number][])}
  {@const max = Math.max(1, ...rows.map((row) => row[1]))}
  <ul class="ls-bars">
    {#each rows as [name, value] (name)}
      <li><span class="n" title={name}>{name}</span><span class="b" aria-hidden="true"><i use:css={{ width: `${((value / max) * 100).toFixed(1)}%` }}></i></span><span class="v">{value} min</span></li>
    {/each}
  </ul>
{/snippet}

<div class="ls-nums">
  <section class="cost" class:over aria-label="What it is costing">
    <h4>What it is costing</h4>
    <span class="big">{rate}</span>
    <p class="proj">{#if over}<Signal word="projected over" /> {/if}{projection}</p>
    <p class="ls-note">{money(spent)} so far · projected from rounds, not the clock</p>
  </section>
  <section aria-label="Where the time went">
    <h4>Where the time went</h4>
    <p class="sub">By round</p>
    {@render bars(byRound)}
    <p class="sub">By node</p>
    {@render bars(byNode)}
  </section>
  <section aria-label="What went wrong">
    <h4>What went wrong</h4>
    <ul class="wrong">
      {#each wrong as [what, count] (what)}
        <li class:none={!count}>{what}<b>{count ? String(count) : 'none'}</b></li>
      {/each}
    </ul>
  </section>
</div>
