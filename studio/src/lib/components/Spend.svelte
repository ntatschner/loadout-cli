<script lang="ts">
  // The spend line: what a run has spent, of what, and where it is projected
  // to end. Past its budget is said in words with a Needs edge, never in
  // colour alone.
  import { css } from '../css';
  import { money } from '../teams';

  interface Props {
    spent: number;
    cap?: number | null;
    uncapped?: boolean;
    projected?: number | null;
    rate?: string | null;
  }

  let { spent, cap = null, uncapped = false, projected = null, rate = null }: Props = $props();

  const limit = $derived(uncapped ? null : cap);
  const over = $derived(!!(limit && projected && projected > limit));
  const percent = $derived(limit ? Math.min(100, (spent / limit) * 100) : 0);
</script>

<div class="ls-spend" class:over>
  <div class="w">
    <b>{money(spent)}</b>
    <span>{limit ? `of ${money(limit)}` : 'no cap, on purpose'}</span>
    {#if rate}<span>· {rate}</span>{/if}
    {#if over}<em>projected {money(projected)}, past its budget</em>{/if}
  </div>
  {#if limit}
    <div
      class="bar"
      role="meter"
      aria-label="Spent"
      aria-valuemin="0"
      aria-valuemax={limit}
      aria-valuenow={spent}
      aria-valuetext={`${money(spent)} of ${money(limit)}${over ? `, projected ${money(projected)}, past its budget` : ''}`}
    >
      <i use:css={{ width: `${percent.toFixed(1)}%` }}></i>
      {#if !over && projected}<u use:css={{ left: `${Math.min(100, (projected / limit) * 100).toFixed(1)}%` }}></u>{/if}
    </div>
  {/if}
</div>
