<script lang="ts">
  // A run's journal or a node's stream, one line each: when, what kind, what
  // was said, and how many times a repeat came round.
  import type { JournalLine } from '../teams';


  let { lines, label = 'Journal' }: { lines: JournalLine[]; label?: string } = $props();
</script>

<!-- svelte-ignore a11y_no_noninteractive_tabindex -->
<ol class="log" tabindex="0" aria-label={label}>
  {#each lines as line, index (index)}
    <li class={[line.family ?? '', line.sub ? 'sub' : ''].filter(Boolean).join(' ') || undefined}>
      <span class="tm">{line.time}</span>
      <span class="tx">{#if line.kind}<span class="k">{line.kind}</span>{/if}{line.text}{#if line.repeats}<span class="x">×{line.repeats}</span>{/if}</span>
    </li>
  {/each}
</ol>
