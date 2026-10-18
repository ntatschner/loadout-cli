<script lang="ts">
  // What a run of a team follows: each rule, its value, and where there is
  // one, the command that changes it.
  import Command from './Command.svelte';

  let { rows }: { rows: [string, string, string?][] } = $props();
</script>

<dl class="ls-rules">
  {#each rows as [term, value, command], index (index)}
    <dt>{term}</dt>
    <dd>{value}{#if command}<Command {command} />{/if}</dd>
  {/each}
</dl>
