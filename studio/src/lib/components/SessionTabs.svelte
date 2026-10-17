<script lang="ts">
  // Sessions as tabs: each its state, title, where it runs and its Ctrl+Alt
  // number, with a dot for output since you last looked. The arrow keys move
  // along the tabs; double-clicking one shows it alone.
  import Signal from './Signal.svelte';
  import { signalOf } from '../signals';
  import type { DeckSession } from '../modes';

  interface Props {
    sessions: DeckSession[];
    current: string | null;
    controls?: string;
    label?: string;
    onpick?: (id: string) => void;
    onmaximise?: (id: string) => void;
    onnew?: () => void;
  }

  let { sessions, current, controls = undefined, label = 'Sessions', onpick = undefined, onmaximise = undefined, onnew = undefined }: Props = $props();

  let tabs = $state<(HTMLButtonElement | undefined)[]>([]);

  function keys(event: KeyboardEvent, index: number) {
    const step: Record<string, number> = { ArrowRight: 1, ArrowLeft: -1 };
    let to: number | null = null;

    if (event.key in step) {
      to = (index + step[event.key] + sessions.length) % sessions.length;
    } else if (event.key === 'Home') {
      to = 0;
    } else if (event.key === 'End') {
      to = sessions.length - 1;
    }

    if (to === null) {
      return;
    }

    event.preventDefault();
    tabs[to]?.focus();
    onpick?.(sessions[to].id);
  }
</script>

<div class="ls-stabs">
  <div role="tablist" aria-label={label}>
    {#each sessions as one, index (one.id)}
      <button
        bind:this={tabs[index]}
        type="button"
        role="tab"
        class:needs={signalOf(one.word).family === 'needs'}
        aria-selected={one.id === current ? 'true' : 'false'}
        tabindex={one.id === current ? 0 : -1}
        aria-controls={controls}
        aria-keyshortcuts={index < 9 ? `Control+Alt+${index + 1}` : undefined}
        data-id={one.id}
        onclick={() => onpick?.(one.id)}
        ondblclick={() => onmaximise?.(one.id)}
        onkeydown={(event) => keys(event, index)}
      >
        <Signal word={one.word} />
        <span class="t">{one.title}</span>
        <span class="x">{[one.where, one.agent].filter(Boolean).join(' · ')}</span>
        {#if index < 9}<span class="k" aria-hidden="true">⌃⌥{index + 1}</span>{/if}
        {#if one.unread}<span class="new" title="New output since you looked"><span class="ls-vh">new output</span></span>{/if}
      </button>
    {/each}
  </div>
  {#if onnew}
    <button type="button" class="add" aria-label="New session" title="New session" onclick={onnew}>+</button>
  {/if}
</div>
