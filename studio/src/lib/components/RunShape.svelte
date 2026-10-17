<script lang="ts">
  // A run's shape in the Map's grammar: the lead's line runs through the
  // rounds as stations, and the nodes each round briefed hang from it. Drawn
  // in the accent, never the Map's line colours, which mean identity there.
  // It turns vertical below 620px of its own width (a container query).
  import Signal from './Signal.svelte';
  import { signalOf } from '../signals';

  interface Round {
    word: string;
    /** What the lead did that round. */
    lead?: string;
    /** [node, state] for each node the round briefed. */
    nodes?: [string, string][];
  }

  interface Props {
    rounds: Round[];
    /** What the next round is for, while one is coming. */
    next?: string | null;
    end?: { word: string; text?: string } | null;
    picked?: string | null;
    onpick?: (node: string) => void;
    label?: string;
  }

  let { rounds, next = null, end = null, picked = null, onpick = undefined, label = 'Rounds' }: Props = $props();

  function place(word: string) {
    const family = signalOf(word).family;

    return word === 'round running' || word === 'running' ? 'cur' : family === 'needs' ? 'wait' : family === 'bad' ? 'bad' : '';
  }
</script>

<div class="ls-shape">
  <ol aria-label={label}>
    {#each rounds as round, index (index)}
      <li class={`rd ${place(round.word)}`}>
        <span class="stn" aria-hidden="true">{index + 1}</span>
        <span class="rl">Round {index + 1}<Signal word={round.word} /></span>
        <p class="lead">{round.lead ?? ''}</p>
        {#if round.nodes?.length}
          <ul class="br" aria-label={`Briefed in round ${index + 1}`}>
            {#each round.nodes as [node, word] (node)}
              <li>
                {#if onpick}
                  <button type="button" aria-pressed={picked === node ? 'true' : 'false'} onclick={() => onpick?.(node)}><span class="n">{node}</span><Signal {word} /></button>
                {:else}
                  <span class="n">{node}</span><Signal {word} />
                {/if}
              </li>
            {/each}
          </ul>
        {/if}
      </li>
    {/each}
    {#if next}
      <li class="rd next">
        <span class="stn" aria-hidden="true">{rounds.length + 1}</span>
        <span class="rl">Next round</span>
        <p class="lead">{next}</p>
      </li>
    {/if}
    {#if end}
      {@const family = signalOf(end.word).family}
      <li class={`rd end ${family}`}>
        <span class="stn" aria-hidden="true">{family === 'good' ? '✓' : '■'}</span>
        <span class="rl">Ended<Signal word={end.word} /></span>
        <p class="lead">{end.text ?? ''}</p>
      </li>
    {/if}
  </ol>
</div>
