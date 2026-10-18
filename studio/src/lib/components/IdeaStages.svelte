<script lang="ts">
  // Captured, Answering, Ready, Proposed, Accepted: where an idea stands, as
  // idea list names it, with its state word under the stage it is at. On a
  // phone the names give way to the dots and one line under them.
  import { signalOf } from '../signals';
  import { STAGES, stageIndex } from '../ideas';

  let { stage, word = undefined }: { stage: string; word?: string } = $props();

  const at = $derived(stageIndex(stage));
  const family = $derived(word ? signalOf(word).family : '');
</script>

<div class="ls-istages-wrap">
  <ol class="ls-istages" aria-label="Where it stands">
    {#each STAGES as name, index (name)}
      <li class={[index < at ? 'past' : '', index === at ? 'now' : '', index === at && (family === 'needs' || family === 'bad') ? family : ''].filter(Boolean).join(' ')} aria-current={index === at ? 'step' : undefined} aria-label={index === at && word ? `${name}, ${word}` : name}>
        <span class="dot" aria-hidden="true"></span>
        <span class="nm">{name}</span>
        {#if index === at && word}<span class="w">{word}</span>{/if}
      </li>
    {/each}
  </ol>
  <p class="cap" aria-hidden="true">Stage {at + 1} of 5: <b>{STAGES[at]}</b>{word ? ` · ${word}` : ''}</p>
</div>
