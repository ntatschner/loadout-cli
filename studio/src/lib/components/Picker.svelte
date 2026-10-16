<script lang="ts">
  // One value from a list, in a popover hanging from the control on a light
  // tether. Arrow keys, Home and End move; Enter or Space chooses; Escape
  // closes and returns focus; a click outside closes it.
  import { tick } from 'svelte';
  import type { PickerOption } from '../types';
  import { fullMotion } from '../studio';

  interface Props {
    label: string;
    options: PickerOption[];
    selected?: number;
    onpick?: (option: PickerOption, index: number) => void;
  }

  let { label, options, selected = $bindable(0), onpick = undefined }: Props = $props();

  const id = `ls-pk-${Math.random().toString(36).slice(2, 7)}`;
  let open = $state(false);
  let active = $state(0);
  let trigger = $state<HTMLButtonElement | undefined>();
  let list = $state<HTMLDivElement | undefined>();
  let anchor = $state<HTMLDivElement | undefined>();

  const current = $derived(options[selected]);

  async function show() {
    active = Math.min(selected, options.length - 1);
    open = true;
    await tick();
    list?.focus();
    document.addEventListener('mousedown', outside);
  }

  function hide(refocus = true) {
    if (!open) {
      return;
    }

    open = false;
    document.removeEventListener('mousedown', outside);

    if (refocus) {
      trigger?.focus();
    }
  }

  function outside(event: MouseEvent) {
    if (anchor && !anchor.contains(event.target as Node)) {
      hide(false);
    }
  }

  function choose(index: number) {
    selected = index;
    hide();
    onpick?.(options[index], index);
  }

  function keys(event: KeyboardEvent) {
    const count = options.length;

    if (event.key === 'Escape' || event.key === 'Tab') {
      if (event.key === 'Escape') {
        event.preventDefault();
      }

      hide(event.key === 'Escape');

      return;
    }

    if (event.key === 'Home') {
      event.preventDefault();
      active = 0;
    } else if (event.key === 'End') {
      event.preventDefault();
      active = count - 1;
    } else if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      choose(active);
    } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      active = (active + (event.key === 'ArrowDown' ? 1 : -1) + count) % count;
    }
  }
</script>

<div class="ls-field">
  <span class="lab" id={`${id}-l`}>{label}</span>
  <div class="ls-anchor" bind:this={anchor}>
    <button
      bind:this={trigger}
      type="button"
      id={`${id}-t`}
      class="ls-picker"
      aria-haspopup="listbox"
      aria-expanded={open ? 'true' : 'false'}
      aria-labelledby={`${id}-l ${id}-t`}
      onclick={() => (open ? hide() : show())}
      onkeydown={(event) => {
        if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && !open) {
          event.preventDefault();
          show();
        }
      }}
    ><span>{current?.name ?? ''}</span><span class="s">{current?.sub ?? ''}</span><span class="chev" aria-hidden="true">▾</span></button>
    {#if open}
      <div
        bind:this={list}
        class="ls-pop"
        class:anim={fullMotion()}
        role="listbox"
        tabindex="-1"
        aria-labelledby={`${id}-l`}
        aria-activedescendant={`${id}-o${active}`}
        onkeydown={keys}
      >
        {#each options as option, index (index)}
          <!-- svelte-ignore a11y_click_events_have_key_events -->
          <div
            role="option"
            id={`${id}-o${index}`}
            class="ls-opt"
            class:is-active={index === active}
            aria-selected={index === selected ? 'true' : 'false'}
            tabindex="-1"
            onclick={() => choose(index)}
            onmousemove={() => (active = index)}
          ><span class="g" aria-hidden="true">{option.glyph ?? (index === selected ? '✓' : '')}</span><span class="t">{option.name}</span>{#if option.sub}<span class="s">{option.sub}</span>{/if}</div>
        {/each}
      </div>
    {/if}
  </div>
</div>
