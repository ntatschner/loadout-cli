<script lang="ts">
  // One panel of a Home view, with its numbered heading. While the layout is
  // being edited it gains its chrome: a grip to drag it by its name, arrows to
  // nudge it earlier or later, and Hide.
  import type { Snippet } from 'svelte';

  interface Props {
    id: string;
    name: string;
    number?: string;
    count?: number;
    wide?: boolean;
    /** Heading for screen readers only, outside editing. */
    quiet?: boolean;
    editing?: boolean;
    first?: boolean;
    last?: boolean;
    dragging?: boolean;
    onmove: (by: number) => void;
    onhide: () => void;
    ondragstart: () => void;
    ondrop: () => void;
    children: Snippet;
  }

  let { id, name, number = '', count = undefined, wide = false, quiet = false, editing = false, first = false, last = false, dragging = false, onmove, onhide, ondragstart, ondrop, children }: Props = $props();

  let over = $state(false);
</script>

<section
  class={`panel p-${id}`}
  class:wide
  class:editing
  class:dragging
  class:over
  aria-labelledby={`z-${id}`}
  ondragover={(event) => {
    if (editing) {
      event.preventDefault();
      over = true;
    }
  }}
  ondragleave={() => (over = false)}
  ondrop={(event) => {
    event.preventDefault();
    over = false;
    ondrop();
  }}
>
  {#if editing}
    <div class="chrome">
      <span
        class="grip"
        role="button"
        tabindex="-1"
        draggable="true"
        aria-label={`Drag ${name}`}
        ondragstart={(event) => {
          event.dataTransfer?.setData('text/plain', id);
          ondragstart();
        }}
      ><span aria-hidden="true">⠿</span>{name}</span>
      <span class="nudge" role="group" aria-label={`Move ${name}`}>
        <button type="button" aria-label={`Move ${name} earlier`} disabled={first} onclick={() => onmove(-1)}>←</button>
        <button type="button" aria-label={`Move ${name} up`} disabled={first} onclick={() => onmove(-1)}>↑</button>
        <button type="button" aria-label={`Move ${name} down`} disabled={last} onclick={() => onmove(1)}>↓</button>
        <button type="button" aria-label={`Move ${name} later`} disabled={last} onclick={() => onmove(1)}>→</button>
      </span>
      <button type="button" class="hidebtn" onclick={onhide}>Hide</button>
    </div>
  {/if}
  <!-- The Map's panels say what they are themselves, so their headings are
       for screen readers until the layout is being edited. -->
  <h2 class={quiet && !editing ? 'ls-sr' : 'ls-zone'} id={`z-${id}`}>{#if number}<span class="no">{number}</span>{/if}{name}{count === undefined ? '' : ` · ${count}`}</h2>
  {@render children()}
</section>
