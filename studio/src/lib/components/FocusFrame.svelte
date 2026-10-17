<script lang="ts">
  // One session or one run, the whole screen below the mode bar. It grows
  // out of the line you opened under full motion, fades under reduced, and
  // simply appears under none. Escape, or Back, gives the page back with
  // the keyboard where it was.
  import { onMount, type Snippet } from 'svelte';
  import Signal from './Signal.svelte';
  import { css } from '../css';
  import { motionNow } from '../modes';

  interface Props {
    title: string;
    sub?: string;
    word?: string;
    /** The line it was opened from, for growing out of. */
    from?: DOMRect | null;
    closeLabel?: string;
    /** A selector for what takes the keyboard first; Back where none is given. */
    initialFocus?: string;
    onclose?: () => void;
    children?: Snippet;
  }

  let { title, sub = undefined, word = undefined, from = null, closeLabel = 'Back', initialFocus = undefined, onclose = undefined, children = undefined }: Props = $props();

  let frame = $state<HTMLElement | undefined>();
  let close = $state<HTMLButtonElement | undefined>();
  const back = typeof document === 'undefined' ? null : (document.activeElement as HTMLElement | null);
  const motion = motionNow();
  // svelte-ignore state_referenced_locally
  const grows = !!from && motion === 'full';
  // svelte-ignore state_referenced_locally
  const place = grows && from
    ? {
        '--ft': `${Math.max(0, from.top)}px`,
        '--fl': `${Math.max(0, from.left)}px`,
        '--fr': `${Math.max(0, window.innerWidth - from.right)}px`,
        '--fb': `${Math.max(0, window.innerHeight - from.bottom)}px`,
      }
    : {};

  let done = false;

  export function leave() {
    if (done) {
      return;
    }

    done = true;
    onclose?.();

    if (back && document.contains(back)) {
      back.focus();
    }
  }

  function keys(event: KeyboardEvent) {
    const target = event.target as HTMLElement | null;

    if (event.key === 'Escape' && !event.defaultPrevented && !target?.closest?.('.ls-dialog')) {
      event.preventDefault();
      leave();
    }
  }

  onMount(() => {
    document.addEventListener('keydown', keys);
    const first = setTimeout(() => {
      const wanted = initialFocus ? frame?.querySelector<HTMLElement>(initialFocus) : null;
      (wanted ?? close)?.focus();
    }, 40);

    return () => {
      clearTimeout(first);
      document.removeEventListener('keydown', keys);
    };
  });
</script>

<div bind:this={frame} class="ls-focus" class:from={grows} class:fade={!grows && motion !== 'none'} role="region" aria-label={title} use:css={place}>
  <div class="fhead">
    <div class="who">
      <h1>{title}</h1>
      {#if sub}<span>{sub}</span>{/if}
    </div>
    {#if word}<Signal {word} />{/if}
    <button bind:this={close} type="button" class="close" aria-keyshortcuts="Escape" onclick={leave}><span aria-hidden="true">×</span>{closeLabel}</button>
  </div>
  <div class="fbody">
    {@render children?.()}
  </div>
</div>
