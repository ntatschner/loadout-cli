<script lang="ts">
  // The modes as radios, with a light that travels to the one picked: 900ms
  // on ease-travel, flaring once as it lands. The arrow keys, Home and End
  // move between them; Ctrl+Shift+M, bound by the app, cycles.
  import { onMount, tick } from 'svelte';
  import { css } from '../css';
  import { MODES, type ModeInfo } from '../modes';

  interface Props {
    mode: string;
    modes?: ModeInfo[] | { key: string; name: string; desc: string; icon: string }[];
    onchange?: (mode: string) => void;
    compact?: boolean;
    vertical?: boolean;
    label?: string;
  }

  let { mode, modes = MODES, onchange = undefined, compact = false, vertical = false, label = 'Mode' }: Props = $props();

  let host = $state<HTMLDivElement | undefined>();
  let buttons = $state<(HTMLButtonElement | undefined)[]>([]);
  let light = $state<Record<string, string>>({});
  let moved = $state(false);
  let placed = false;

  /** Moves the light onto the picked one; the first placement lands without travelling. */
  async function place(animate: boolean) {
    await tick();
    const picked = buttons[modes.findIndex((one) => one.key === mode)];

    if (!picked || !picked.offsetWidth) {
      return;
    }

    const at: Record<string, string> = vertical
      ? { transform: `translateY(${picked.offsetTop}px)`, height: `${picked.offsetHeight}px` }
      : { transform: `translateX(${picked.offsetLeft}px)`, width: `${picked.offsetWidth}px` };

    light = placed && animate ? at : { ...at, transition: 'none' };

    if (!placed || !animate) {
      requestAnimationFrame(() => (light = at));
    }

    placed = true;

    if (animate) {
      // The flare as it lands: the class off and on again so it plays each time.
      moved = false;
      requestAnimationFrame(() => (moved = true));
    }
  }

  $effect(() => {
    void mode;
    void place(placed);
  });

  onMount(() => {
    if (typeof ResizeObserver === 'undefined') {
      return;
    }

    const watcher = new ResizeObserver(() => void place(false));

    if (host) {
      watcher.observe(host);
    }

    return () => watcher.disconnect();
  });

  function pick(key: string, focus: boolean) {
    if (key !== mode) {
      onchange?.(key);
    }

    if (focus) {
      void tick().then(() => buttons[modes.findIndex((one) => one.key === key)]?.focus());
    }
  }

  function keys(event: KeyboardEvent, index: number) {
    const step: Record<string, number> = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 };
    let to: number | null = null;

    if (event.key in step) {
      to = (index + step[event.key] + modes.length) % modes.length;
    } else if (event.key === 'Home') {
      to = 0;
    } else if (event.key === 'End') {
      to = modes.length - 1;
    }

    if (to === null) {
      return;
    }

    event.preventDefault();
    pick(modes[to].key, true);
  }
</script>

<div bind:this={host} class="ls-modes" class:compact class:vertical class:moved role="radiogroup" aria-label={label}>
  <span class="light" aria-hidden="true" use:css={light}></span>
  {#each modes as one, index (one.key)}
    <button
      bind:this={buttons[index]}
      type="button"
      role="radio"
      data-mode={one.key}
      title={one.desc}
      aria-checked={one.key === mode ? 'true' : 'false'}
      aria-keyshortcuts="Control+Shift+M"
      tabindex={one.key === mode ? 0 : -1}
      onclick={() => pick(one.key, true)}
      onkeydown={(event) => keys(event, index)}
    >
      <svg viewBox="0 0 20 20" aria-hidden="true"><path d={one.icon} fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" /></svg>
      <span class="n">{one.name}</span>
    </button>
  {/each}
</div>
