<script lang="ts">
  // A run's rounds as a line small enough for a board row: a stop for each
  // round with a tick per node, the one running lit, and a dashed stop for
  // what comes next or a solid one where it ended. Decoration: the row
  // beside it says the same in words.
  import { signalOf } from '../signals';

  interface Props {
    rounds: { word: string; nodes?: [string, string][] }[];
    end?: boolean;
    running?: boolean;
  }

  let { rounds, end = false, running = false }: Props = $props();

  const stopOf = (word: string) => {
    const family = signalOf(word || 'done').family;

    return word === 'running' || word === 'round running' ? 'cur' : family === 'needs' ? 'wait' : family === 'bad' ? 'bad' : '';
  };
</script>

<div class="ls-mini" aria-hidden="true">
  {#each rounds as round, index (index)}
    {#if index}<span class="seg" class:into={stopOf(round.word) === 'cur'}></span>{/if}
    <span class={`st ${stopOf(round.word)}`}>
      <i></i>
      <span class="nd">
        {#each round.nodes ?? [] as [name, word] (name)}
          <u class={signalOf(word).family} title={`${name} · ${word}`}></u>
        {/each}
      </span>
    </span>
  {/each}
  {#if running}
    <span class="seg dash"></span><span class="st next"><i></i></span>
  {:else if end}
    <span class="seg"></span><span class="st end"><i></i></span>
  {/if}
</div>
