<script lang="ts">
  // Minimal: one column of what is live. What needs you comes first, with
  // its question said under it; the rest quietly after.
  import Signal from './Signal.svelte';
  import { css } from '../css';
  import { signalOf } from '../signals';
  import type { QuietItem } from '../modes';

  interface Props {
    items: QuietItem[];
    label?: string;
    /** Told which was opened, and where its line was, for the frame to grow out of. */
    onopen?: (item: QuietItem, from: DOMRect) => void;
  }

  let { items, label = 'Everything live', onopen = undefined }: Props = $props();

  const needs = $derived(items.filter((item) => signalOf(item.word).family === 'needs'));
  const rest = $derived(items.filter((item) => signalOf(item.word).family !== 'needs'));
</script>

{#snippet row(item: QuietItem, calm: boolean, index: number)}
  <li class={calm ? 'calm' : 'needs'} use:css={{ '--i': String(index) }}>
    <button type="button" onclick={(event) => onopen?.(item, (event.currentTarget as HTMLElement).getBoundingClientRect())}>
      <span class="t">{item.title}</span>
      <span class="x">{[item.kind, item.where].filter(Boolean).join(' · ')}</span>
      <Signal word={item.word} />
    </button>
    {#if !calm && item.ask}<p class="ask">{item.ask}</p>{/if}
  </li>
{/snippet}

<ol class="ls-quiet" aria-label={label}>
  {#if needs.length}
    <li class="zone" aria-hidden="true">Needs you</li>
    {#each needs as item, index (item.kind + item.id)}
      {@render row(item, false, index)}
    {/each}
  {/if}
  {#if rest.length}
    <li class="zone" aria-hidden="true">{needs.length ? 'The rest' : 'Live'}</li>
    {#each rest as item, index (item.kind + item.id)}
      {@render row(item, true, needs.length + index)}
    {/each}
  {/if}
  {#if !items.length}
    <li class="calm"><p class="ask quietnone">Nothing is live.</p></li>
  {/if}
</ol>
