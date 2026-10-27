<script lang="ts" module>
  let made = 0;
</script>

<script lang="ts">
  // The field map behind every page, mounted once. It draws only when it is
  // first wanted, after the first paint, and again 250ms after a resize; a
  // change of setting, theme or mode never redraws it, since the CSS decides
  // from data-fieldmap what shows. Every attribute here is SVG geometry or a
  // class: Studio's policy refuses inline styles.
  import { onMount } from 'svelte';
  import { fieldMap } from '../fieldmap';
  import type { FieldMapShown } from '../backdrop';

  let { shown }: { shown: FieldMapShown } = $props();

  const id = `ls-fm${++made}`;
  let size = $state<{ width: number; height: number } | null>(null);
  const map = $derived(size ? fieldMap(size.width, size.height) : null);

  const measure = () => ({ width: window.innerWidth, height: window.innerHeight });

  /** Runs `then` once the browser is idle, or a frame from now where it cannot say. */
  function later(then: () => void): () => void {
    if (typeof requestIdleCallback === 'function') {
      const handle = requestIdleCallback(then, { timeout: 1000 });

      return () => cancelIdleCallback(handle);
    }

    const handle = requestAnimationFrame(then);

    return () => cancelAnimationFrame(handle);
  }

  $effect(() => {
    if (shown === 'plain' || size) {
      return;
    }

    return later(() => (size = measure()));
  });

  onMount(() => {
    let timer: ReturnType<typeof setTimeout> | undefined;
    const resized = () => {
      clearTimeout(timer);
      // While the map is hidden, forget the size, so it is drawn afresh when it next shows.
      timer = setTimeout(() => (size = shown === 'plain' ? null : measure()), 250);
    };

    window.addEventListener('resize', resized);

    return () => {
      clearTimeout(timer);
      window.removeEventListener('resize', resized);
    };
  });
</script>

<div class="ls-fieldmap" aria-hidden="true">
  {#if map && size}
    <svg viewBox="0 0 {size.width} {size.height}" preserveAspectRatio="none" focusable="false">
      <defs>
        <path id="{id}-minor" d={map.minor} />
        <path id="{id}-index" d={map.index} />
      </defs>
      <g>
        {#each map.columns as x (x)}
          <line class="grid" x1={x} y1="0" x2={x} y2={size.height} stroke-width="1" />
        {/each}
        {#each map.rows as y (y)}
          <line class="grid" x1="0" y1={y} x2={size.width} y2={y} stroke-width="1" />
        {/each}
      </g>
      <g>
        <use href="#{id}-minor" class="lo" fill="none" stroke-width="1.1" stroke-linecap="round" transform="translate(0.7 0.7)" />
        <use href="#{id}-minor" class="hi" fill="none" stroke-width="1" stroke-linecap="round" transform="translate(-0.6 -0.6)" />
        <use href="#{id}-minor" class="ln" fill="none" stroke-width="0.9" stroke-linecap="round" />
      </g>
      <g class="ixg">
        <use href="#{id}-index" class="lo" fill="none" stroke-width="1.8" stroke-linecap="round" transform="translate(0.9 0.9)" />
        <use href="#{id}-index" class="hi" fill="none" stroke-width="1.5" stroke-linecap="round" transform="translate(-0.8 -0.8)" />
        <use href="#{id}-index" class="ix" fill="none" stroke-width="1.3" stroke-linecap="round" />
      </g>
      <g>
        {#each map.references as ref (`${ref.x},${ref.y}`)}
          <text class="ref" x={ref.x} y={ref.y} text-anchor={ref.anchor}>{ref.text}</text>
        {/each}
      </g>
    </svg>
    <!-- The scan covers the page area only, from the rail's collapsed edge, so it never shows through the rail. -->
    <div class="scanzone">
      <svg class="scan" viewBox="0 0 {size.width} {size.height}" preserveAspectRatio="none" focusable="false">
        <use href="#{id}-minor" class="ln" fill="none" stroke-width="1.1" stroke-linecap="round" />
        <use href="#{id}-index" class="ix" fill="none" stroke-width="1.6" stroke-linecap="round" />
      </svg>
    </div>
  {/if}
</div>
