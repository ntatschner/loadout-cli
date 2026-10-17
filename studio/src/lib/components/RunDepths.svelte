<script lang="ts">
  // The five depths of a run, on keys 1 to 5: what it said, what it cost,
  // the papers, what it changed and what it delivered. The page fills each
  // depth when it is opened; this draws the tabs and binds the keys, which
  // leave alone anything typed into a field, a dialog or a terminal.
  import type { Snippet } from 'svelte';
  import { roving } from '../roving';
  import { DEPTHS, depthKey } from '../teams';

  interface Props {
    selected?: number;
    /** Whether 1 to 5 pick a depth; off where Studio's single-letter shortcuts are. */
    keys?: boolean;
    onselect?: (index: number) => void;
    children: Snippet<[number]>;
  }

  let { selected = $bindable(0), keys = true, onselect = undefined, children }: Props = $props();

  const id = `ls-depths-${Math.random().toString(36).slice(2, 8)}`;
  let buttons = $state<(HTMLButtonElement | undefined)[]>([]);

  function select(index: number) {
    selected = index;
    onselect?.(index);
  }

  function pressed(event: KeyboardEvent) {
    if (!keys) {
      return;
    }

    const index = depthKey(event);

    if (index === null) {
      return;
    }

    event.preventDefault();
    select(index);
    buttons[index]?.focus();
  }
</script>

<svelte:document onkeydown={pressed} />

<div class="ls-depths">
  <div class="ls-tabs" role="tablist" aria-label="Five depths of this run">
    {#each DEPTHS as label, index (label)}
      <button
        bind:this={buttons[index]}
        type="button"
        role="tab"
        id={`${id}-${index}`}
        aria-controls={`${id}-panel`}
        aria-selected={index === selected ? 'true' : 'false'}
        aria-keyshortcuts={String(index + 1)}
        tabindex={index === selected ? 0 : -1}
        onclick={() => select(index)}
        onkeydown={(event) => roving(event, index, buttons, select)}
      ><span class="k" aria-hidden="true">{index + 1}</span>{label}</button>
    {/each}
  </div>
  <div role="tabpanel" id={`${id}-panel`} tabindex="0" aria-labelledby={`${id}-${selected}`}>
    {@render children(selected)}
  </div>
</div>
