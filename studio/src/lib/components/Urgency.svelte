<script lang="ts" generics="T extends { key: string; waiting?: boolean; problem?: boolean; askedAt?: number }">
  // Waiting items, most urgent first: a problem before a question, an older
  // question before a newer one. The first rises and the rest recede, while
  // `rising` is on; each row is drawn by the caller.
  import type { Snippet } from 'svelte';
  import { rankUrgent } from '../signals';
  import { urgencyClass } from '../urgency';

  interface Props {
    items: T[];
    /** Raise the first: on when something is waiting and the page is not being edited. */
    rising?: boolean;
    label?: string;
    row: Snippet<[T, number]>;
    empty?: Snippet;
  }

  let { items, rising = true, label = 'Needs you', row, empty = undefined }: Props = $props();

  const ranked = $derived(rankUrgent(items));
  const level = $derived(ranked[0]?.problem ? 'problem' : 'needs');
</script>

{#if ranked.length}
  <ul class="ls-urgent" aria-label={label}>
    {#each ranked as item, index (item.key)}
      <li class={urgencyClass(index, rising ? 0 : -1, level)}>{@render row(item, index)}</li>
    {/each}
  </ul>
{:else}
  {@render empty?.()}
{/if}
