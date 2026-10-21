<script lang="ts">
  // What you are watching, and a page beside it when one is open. The
  // watched side is always the same element in the same place, so opening
  // or closing a page never makes it again: the terminals keep their
  // connections and scrollback. A divider between them sets how much each
  // gets. Below 1100px the page takes the width, and the divider goes.
  import type { Snippet } from 'svelte';
  import Splitter from './Splitter.svelte';
  import { clampSplit, floorOf, terminalFloor, tracks } from '../splits';

  interface Props {
    focus: Snippet;
    beside?: Snippet;
    /** How much of the width the page beside was left with, kept by the page; null for the default. */
    split?: number | null;
    onsplit?: (beside: number) => void;
  }

  let { focus, beside = undefined, split = null, onsplit = undefined }: Props = $props();

  // The page beside never goes below what it was always given, and what you
  // watch never below a terminal's floor.
  const GAP = 18;
  const PAGE_FLOOR = 460;
  const DEFAULT = 0.52;
  let wide = $state(0);
  const length = $derived(wide - GAP);
  const min = $derived(floorOf(terminalFloor().width, length));
  const max = $derived(1 - floorOf(PAGE_FLOOR, length));
  // The divider's place is where what you watch ends.
  const at = $derived(clampSplit(1 - (split ?? DEFAULT), min, max));
</script>

<div
  class={['ls-besidegrid', beside ? '' : 'solo'].filter(Boolean).join(' ')}
  bind:clientWidth={wide}
  style:--beside-cols={beside && split !== null ? tracks(at) : undefined}
>
  <div class="focusarea">{@render focus()}</div>
  {#if beside}
    {@render beside()}
    <Splitter label="Width of the page beside" value={at} {min} {max} reset={1 - DEFAULT} onchange={(to) => onsplit?.(1 - to)} />
  {/if}
</div>
