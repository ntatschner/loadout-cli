<script lang="ts">
  // One of the six pages in Minimal: its name, one sentence with what needs
  // you in bold, what needs you or has gone wrong as a QuietList, and Show
  // everything, which opens the page as it is in Default below. That is
  // built the first time it is opened, and kept.
  import type { Snippet } from 'svelte';
  import QuietList from './QuietList.svelte';
  import type { QuietItem } from '../modes';

  interface Props {
    title: string;
    sentence?: Snippet;
    items: QuietItem[];
    onopen?: (item: QuietItem, from: DOMRect) => void;
    everything: Snippet;
    calmText?: string;
    /** Opened at once, for a deep link. */
    open?: boolean;
  }

  let { title, sentence = undefined, items, onopen = undefined, everything, calmText = 'Nothing here needs you.', open = false }: Props = $props();

  // svelte-ignore state_referenced_locally
  let shown = $state(open);
  // svelte-ignore state_referenced_locally
  let built = $state(open);
  const id = `qp-${Math.random().toString(36).slice(2, 7)}`;

  function toggle() {
    shown = !shown;
    built ||= shown;
  }
</script>

<div class="ls-qpage">
  <div class="qh">
    <h1>{title}</h1>
    {#if sentence}<p>{@render sentence()}</p>{/if}
  </div>
  {#if items.length}
    <QuietList {items} label={`What matters on ${title}`} {onopen} />
  {:else}
    <p class="calmnote">✓ {calmText}</p>
  {/if}
  <button type="button" class="more" aria-expanded={shown ? 'true' : 'false'} aria-controls={id} onclick={toggle}>{shown ? 'Show less' : 'Show everything'}</button>
  <div class="all" {id} hidden={!shown}>
    {#if built}{@render everything()}{/if}
  </div>
</div>
