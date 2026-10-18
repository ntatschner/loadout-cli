<script lang="ts">
  // What the agents spent over a period, in one number, billed as input,
  // and what the cache saved, with how it splits four ways. The patterns
  // and the list carry the split, so colour never does alone. No budget
  // words: Spend shows what was spent.
  import { css } from '../css';
  import { tokens } from '../glyphs';

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

  let { totals, since = undefined, complete = true, from = '$ loadout usage' }: { totals: Totals; since?: string; complete?: boolean; from?: string } = $props();

  const pct = (fraction: number) => `${Math.round((Number(fraction) || 0) * 100)}%`;
  const parts = $derived([
    ['input', 'Input', totals.input],
    ['cache', 'Read from the cache', totals.cacheRead],
    ['output', 'Output', totals.output],
    ['thinking', 'Thinking', totals.thinking],
  ] as const);
  const sum = $derived(parts.reduce((total, part) => total + (part[2] || 0), 0) || 1);
</script>

<div class="ls-split">
  <p class="hero"><b>{tokens(totals.billedInputEquivalent)}</b><span>tokens billed as input</span><span class="saved">✓ the cache saved {pct(totals.savedFraction)}</span></p>
  <div class="bar" role="img" aria-label={parts.map((part) => `${part[1]} ${tokens(part[2])}`).join(', ')}>
    {#each parts as part (part[0])}<span class={`k-${part[0]}`} use:css={{ flex: `0 0 calc(${(((part[2] || 0) / sum) * 100).toFixed(3)}% - 2px)` }}></span>{/each}
  </div>
  <dl>
    {#each parts as part (part[0])}
      <div><i class={`k-${part[0]}`} aria-hidden="true"></i><dt>{part[1]}</dt><dd>{tokens(part[2])}{#if part[0] === 'cache'}<span> · {pct(totals.cacheHitFraction)} of input</span>{/if}</dd></div>
    {/each}
  </dl>
  {#if since}
    <p class={['since', complete ? '' : 'needs'].filter(Boolean).join(' ')}>Since {since}{complete ? '' : ' · some transcripts could not be read, so these are a floor'}. From <code class="ls-cmd">{from}</code>.</p>
  {/if}
</div>
