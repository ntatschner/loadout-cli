<script lang="ts">
  // The words in the halo's middle: which line, what it is, its state and,
  // while it asks, the question with its answers.
  import HaloButton from '../lib/components/HaloButton.svelte';
  import Signal from '../lib/components/Signal.svelte';
  import { actionsFor } from '../lib/actions';
  import type { Work } from '../lib/lines';
  import { router } from '../lib/router.svelte';
  import { wordFor } from '../lib/signals';
  import { studio } from '../lib/state.svelte';

  // Answers inside the ring only where asked: a real command is longer than
  // the ring is wide, so Home draws them under the halo instead.
  let { line, answers = true }: { line: Work; answers?: boolean } = $props();

  const actions = $derived(actionsFor(studio, line, router.go.bind(router)));
</script>

<div class="core">
  <span class="k">line {line.code} · {line.kind} · {line.where}</span>
  <p class="t">{line.title}</p>
  <Signal word={wordFor(line, studio.idleAfter)} big />
  <span class="note">{line.blocked ?? line.failed ?? line.note ?? ''}</span>
  {#if line.waiting && line.ask}<p class="ask">{line.ask}</p>{/if}
  {#if answers && actions.length}
    <div class="ls-answers">
      {#each actions as action (action.label)}
        <HaloButton size="small" {...action} />
      {/each}
    </div>
  {/if}
</div>
