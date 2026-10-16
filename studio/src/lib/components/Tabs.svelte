<script lang="ts">
  // Views of the same thing, with a line of light under the one you are on.
  // Arrow keys, Home and End move and select; only the selected tab is in
  // the tab order.
  import type { Snippet } from 'svelte';
  import { roving } from '../roving';

  interface Props {
    tabs: { label: string }[];
    selected?: number;
    /** What the tabs choose between, for screen readers. */
    label?: string;
    id?: string;
    onselect?: (index: number) => void;
    children?: Snippet<[number]>;
  }

  let { tabs, selected = $bindable(0), label = undefined, id = `ls-tabs-${Math.random().toString(36).slice(2, 7)}`, onselect = undefined, children = undefined }: Props = $props();

  const buttons = $state<(HTMLButtonElement | undefined)[]>([]);

  function select(index: number) {
    selected = index;
    onselect?.(index);
  }
</script>

<div>
  <div class="ls-tabs" role="tablist" aria-label={label}>
    {#each tabs as tab, index (index)}
      <button
        bind:this={buttons[index]}
        type="button"
        role="tab"
        id={`${id}-${index}`}
        aria-controls={`${id}-panel`}
        aria-selected={index === selected ? 'true' : 'false'}
        tabindex={index === selected ? 0 : -1}
        onclick={() => select(index)}
        onkeydown={(event) => roving(event, index, buttons, select)}
      >{tab.label}</button>
    {/each}
  </div>
  <div role="tabpanel" id={`${id}-panel`} tabindex="0" aria-labelledby={`${id}-${selected}`}>
    {@render children?.(selected)}
  </div>
</div>
