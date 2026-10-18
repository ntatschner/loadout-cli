<script lang="ts">
  // What you are watching, and a page beside it when one is open. The
  // watched side is always the same element in the same place, so opening
  // or closing a page never makes it again: the terminals keep their
  // connections and scrollback. Below 1100px the page takes the width.
  import type { Snippet } from 'svelte';

  let { focus, beside = undefined }: { focus: Snippet; beside?: Snippet } = $props();
</script>

<div class={['ls-besidegrid', beside ? '' : 'solo'].filter(Boolean).join(' ')}>
  <div class="focusarea">{@render focus()}</div>
  {#if beside}{@render beside()}{/if}
</div>
