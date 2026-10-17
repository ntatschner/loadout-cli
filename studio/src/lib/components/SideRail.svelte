<script lang="ts">
  // Studio's navigation: a narrow rail of page icons down the left edge, each
  // with a live count, that widens over the page with a dim on hover or
  // focus and never pushes it sideways. Below 600px a Pages tab slides the
  // same list in.
  import type { Snippet } from 'svelte';
  import { tick } from 'svelte';
  import type { RailPage } from '../types';

  interface Props {
    pages: RailPage[];
    current: string;
    label?: string;
    narrow?: boolean;
    onsearch?: () => void;
    /** What sits at the foot of the rail, such as the Appearance row. */
    foot?: Snippet;
  }

  let { pages, current, label = 'Studio', narrow = false, onsearch = undefined, foot = undefined }: Props = $props();

  let sliding = $state(false);
  let tab = $state<HTMLButtonElement | undefined>();
  let closeButton = $state<HTMLButtonElement | undefined>();

  const ICONS: Record<string, string> = {
    home: 'M3 10 L10 3 L17 10 M5 8.5 V17 H15 V8.5',
    sessions: 'M3 4.5 H17 V15.5 H3 Z M6 8.5 L8.5 10.5 L6 12.5 M10 12.5 H14',
    ideas: 'M10 2 L15 10 L10 18 L5 10 Z',
    teams: 'M3.5 7a2 2 0 1 0 4 0a2 2 0 1 0-4 0 M12.5 7a2 2 0 1 0 4 0a2 2 0 1 0-4 0 M8 14.5a2 2 0 1 0 4 0a2 2 0 1 0-4 0 M7 8.5 L9 12.5 M13 8.5 L11 12.5',
    memory: 'M4 4.5 H16 M4 8.5 H16 M4 12.5 H16 M4 16.5 H11',
    context: 'M10 3 L17 7 L10 11 L3 7 Z M3 10.5 L10 14.5 L17 10.5 M3 14 L10 18 L17 14',
    spend: 'M10 2 V18 M14 6 C13 4 7 4 7 7 C7 10 13 9.5 13 13 C13 16 7 16.5 6 14',
    projects: 'M3 5.5 H8 L10 7.5 H17 V16 H3 Z',
    search: 'M8.5 3a5.5 5.5 0 1 0 0 11a5.5 5.5 0 1 0 0-11 M12.6 12.6 L17 17',
    settings:
      'M7 10a3 3 0 1 0 6 0a3 3 0 1 0-6 0 M10 2 V5 M10 15 V18 M2 10 H5 M15 10 H18 M4.3 4.3 L6.4 6.4 M13.6 13.6 L15.7 15.7 M15.7 4.3 L13.6 6.4 M6.4 13.6 L4.3 15.7',
  };

  async function slideIn() {
    sliding = true;
    await tick();
    closeButton?.focus();
  }

  function slideOut() {
    sliding = false;
    tab?.focus();
  }
</script>

{#snippet icon(d: string)}
  <svg viewBox="0 0 20 20" aria-hidden="true"><path {d} fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" /></svg>
{/snippet}

{#snippet rows(inSlide: boolean)}
  <button type="button" class="ls-rl search" aria-keyshortcuts="Control+K Meta+K" onclick={() => { if (inSlide) { sliding = false; } onsearch?.(); }}>
    {@render icon(ICONS.search)}<span class="n">Search</span><span class="f">Ctrl K · go to or do</span>
  </button>
  {#each pages as page (page.key)}
    <a
      href={page.href ?? `#${page.key}`}
      class="ls-rl"
      class:need={page.need}
      aria-current={page.key === current ? 'page' : undefined}
      onclick={() => inSlide && (sliding = false)}
    >{@render icon(ICONS[page.key] ?? ICONS.home)}<span class="n">{page.name}</span><span class="f">{page.fact ?? ''}</span><span class="needdot" aria-hidden="true">!</span></a>
  {/each}
{/snippet}

<div class="ls-nav" class:narrow>
  <nav class="ls-railnav" aria-label={label}>
    <div class="mark"><i aria-hidden="true"></i><b>LOADOUT</b></div>
    {@render rows(false)}
    {@render foot?.()}
  </nav>
  <button bind:this={tab} type="button" class="ls-pagestab" aria-expanded={sliding ? 'true' : 'false'} onclick={() => (sliding ? slideOut() : slideIn())}>Pages</button>
  {#if sliding}
    <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
    <div class="ls-slidescrim" onclick={slideOut}></div>
    <!-- Escape from anywhere inside the slid-in list closes it: the key
         arrives from the focused link and is handled here, once. -->
    <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
    <nav
      class="ls-slide"
      aria-label={label}
      onkeydown={(event) => {
        if (event.key === 'Escape') {
          event.stopPropagation();
          slideOut();
        }
      }}
    >
      <button bind:this={closeButton} type="button" class="close" onclick={slideOut}><span aria-hidden="true">×</span>Close</button>
      {@render rows(true)}
      {@render foot?.()}
    </nav>
  {/if}
</div>
