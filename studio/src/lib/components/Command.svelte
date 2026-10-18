<script lang="ts">
  // A loadout command as a block, for the things Studio never does itself:
  // reading a secret back, and agreements made in a terminal. The command is
  // real text, so it can be selected and copied.
  let { command, why = undefined, live = false }: { command: string; why?: string; live?: boolean } = $props();
</script>

<code class="ls-cmdline" aria-live={live ? 'polite' : undefined}>{#if why}<span class="why">{why}</span>{/if}{command}</code>
