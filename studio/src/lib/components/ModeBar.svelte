<script lang="ts">
  // The bar at the top of every mode: the switcher, search, and whatever is
  // waiting on you, sessions and team runs together, so no focus mode ever
  // hides a question. Pressing the count goes to the oldest.
  import ModeSwitcher from './ModeSwitcher.svelte';
  import Signal from './Signal.svelte';
  import type { ModeInfo, Waiting } from '../modes';


  interface Props {
    mode: string;
    modes?: ModeInfo[] | { key: string; name: string; desc: string; icon: string }[];
    onmode?: (mode: string) => void;
    compact?: boolean;
    onsearch?: () => void;
    waiting?: Waiting[];
    onwaiting?: (waiting: Waiting) => void;
  }

  let { mode, modes = undefined, onmode = undefined, compact = false, onsearch = undefined, waiting = [], onwaiting = undefined }: Props = $props();

  const oldest = $derived(waiting.slice().sort((a, b) => a.at - b.at));
</script>

<div class="ls-modebar" role="region" aria-label="Mode">
  <ModeSwitcher {mode} {modes} {compact} onchange={onmode} />
  <span class="grow"></span>
  {#if onsearch}
    <button type="button" class="tool" aria-label="Search: go to or do" aria-keyshortcuts="Control+K Meta+K" title="Ctrl K" onclick={onsearch}>
      <svg viewBox="0 0 20 20" aria-hidden="true"><path d="M8.5 3.5a5 5 0 1 0 0 10a5 5 0 1 0 0-10 M12.2 12.2L16.5 16.5" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" /></svg>
    </button>
  {/if}
  {#if oldest.length}
    <button type="button" class="waiting" onclick={() => onwaiting?.(oldest[0])}>
      <Signal word="waiting for you" />
      <b>{oldest.length}<span class="wn">{oldest.length === 1 ? ' needs you' : ' need you'}</span></b>
      <span class="wl">· {oldest[0].label}</span>
    </button>
  {:else}
    <span class="calmword">nothing waiting on you</span>
  {/if}
</div>
