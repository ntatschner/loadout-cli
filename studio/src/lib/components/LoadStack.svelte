<script lang="ts">
  // What a session on this project would start with: one bar of its loaded
  // specialists against the token budget, why each is there, and what was
  // left out. Hovering or focusing a segment lights its row, and the other
  // way round; each segment is a button named for what it is, so the bar
  // reads without being seen.
  import { css } from '../css';
  import { tokens } from '../glyphs';
  import { stackOf, type Loaded } from '../loadStack';

  interface Props {
    selected: Loaded[];
    omitted?: { id: string; reason: string }[];
    conflicts?: number;
    estimatedTokens?: number;
    tokenBudget?: number;
    nearBudget?: boolean;
    href?: (id: string) => string;
    onpick?: (id: string) => void;
  }

  let { selected, omitted = [], conflicts = 0, estimatedTokens = undefined, tokenBudget = undefined, nearBudget = false, href = undefined, onpick = undefined }: Props = $props();

  const stack = $derived(stackOf(selected, estimatedTokens, tokenBudget, nearBudget));
  let lit = $state<number | null>(null);
  const figure = (n: number) => n.toLocaleString('en-GB');
</script>

<div class="ls-load">
  <p class={['big', stack.standing === 'over' ? 'over' : ''].filter(Boolean).join(' ')}>
    <b>{figure(stack.used)}</b><span>tokens every session starts with</span>
    {#if stack.standing === 'over'}
      <span class="w">· {figure(stack.used - stack.budget)} over the budget of {figure(stack.budget)}</span>
    {:else if stack.standing === 'near'}
      <span class="w">· near the budget of {figure(stack.budget)}</span>
    {:else if stack.standing === 'within'}
      <span class="w">· within the budget of {figure(stack.budget)}</span>
    {/if}
  </p>

  <div class="bar" role="group" aria-label="Loaded specialists, by tokens">
    {#each stack.rows as row, index (row.id)}
      <button type="button" class={['seg', row.always ? 'always' : '', lit === index ? 'lit' : ''].filter(Boolean).join(' ')}
        use:css={{ flex: `0 0 calc(${stack.widths[index].toFixed(3)}% - 2px)` }}
        aria-label={`${row.title}, ${row.estimatedTokens} tokens`}
        onmouseenter={() => (lit = index)} onmouseleave={() => (lit = null)} onfocus={() => (lit = index)} onblur={() => (lit = null)}
        onclick={() => onpick?.(row.id)}></button>
    {/each}
    {#if stack.budget}<span class="mark" aria-hidden="true" use:css={{ left: `calc(${stack.budgetShare.toFixed(3)}% - 1px)` }}></span>{/if}
    {#if stack.standing === 'over'}<span class="over" aria-hidden="true" use:css={{ left: `${stack.budgetShare.toFixed(3)}%`, right: '0' }}></span>{/if}
  </div>
  {#if stack.budget}
    <div class="scale" aria-hidden="true">
      <span class="start" use:css={{ left: '0' }}>0</span>
      <span class={stack.standing === 'over' ? '' : 'end'} use:css={{ left: `${stack.budgetShare.toFixed(3)}%` }}>budget {tokens(stack.budget)}</span>
      {#if stack.standing === 'over'}<span class="end" use:css={{ left: '100%' }}>{tokens(stack.used)}</span>{/if}
    </div>
  {/if}
  <div class="legend" aria-hidden="true">
    <span><i></i>loaded for this task</span>
    <span><i class="always"></i>always loaded</span>
    {#if stack.budget}<span><i class="mk"></i>the budget</span>{/if}
  </div>

  <ol aria-label="Loaded">
    {#each stack.rows as row, index (row.id)}
      <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
      <li class={[row.always ? 'always' : '', lit === index ? 'lit' : ''].filter(Boolean).join(' ')} onmouseenter={() => (lit = index)} onmouseleave={() => (lit = null)}>
        <span class="sw" aria-hidden="true"></span>
        {#if href}<a class="t" href={href(row.id)}>{row.title}</a>{:else}<span class="t">{row.title}</span>{/if}
        <span class="n">{figure(row.estimatedTokens)}</span>
        <span class="r">{[row.id, row.always ? 'always loaded' : null, row.reason].filter(Boolean).join(' · ')}</span>
      </li>
    {/each}
  </ol>

  {#if omitted.length}
    <details>
      <summary>{omitted.length} considered and left out</summary>
      <ul>{#each omitted as one (one.id)}<li><b>{one.id}</b>{one.reason}</li>{/each}</ul>
    </details>
  {/if}
  {#if conflicts}<p class="ls-from">{conflicts} overlap{conflicts === 1 ? '' : 's'} settled; the narrower one wins.</p>{/if}
</div>
