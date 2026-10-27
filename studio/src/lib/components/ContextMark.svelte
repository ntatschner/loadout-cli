<script lang="ts">
  // How much of a session's context window is in use, small enough for a
  // tab or a list row: the figure and a short beam, turning to Needs as it
  // fills and to Bad when it is nearly full. Nothing here is announced as it
  // changes; the strip on the terminal says when a threshold is crossed.
  import { levelOf, levelWords } from '../status';

  let { percent }: { percent: number | null } = $props();

  const level = $derived(levelOf(percent));
  const words = $derived(levelWords(level));
</script>

{#if percent !== null}
  <span
    class={`ls-ctx ${level}`}
    role="img"
    aria-label={`Context ${percent}% used${words ? `, ${words}` : ''}`}
    title={`Context ${percent}% used${words ? `, ${words}` : ''}`}
  ><i class="beam" aria-hidden="true"><i style:width={`${Math.min(100, Math.max(0, percent))}%`}></i></i><b aria-hidden="true">{percent}%</b>{#if level === 'urgent'}<span class="bang" aria-hidden="true">!</span>{/if}</span>
{/if}
