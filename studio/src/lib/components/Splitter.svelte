<script lang="ts">
  // The divider between two panes of a split, for every split Studio has, so
  // they all drag and take keys the same way. It sits in the gap of its
  // grid, placed there by the page's own CSS, and only says where the
  // boundary should be: the page turns that into its tracks. The arrows move
  // it a little, Shift with an arrow further, Home and End to its limits,
  // and Enter or a double click puts it back where it started.
  import { clampSplit, SPLIT_SETTLED } from '../splits';

  interface Props {
    /** Where the boundary sits, as a fraction of the length the panes share. */
    value: number;
    min?: number;
    max?: number;
    /** Where Enter or a double click puts it. */
    reset: number;
    /** vertical between panes side by side, horizontal between one above the other. */
    orientation?: 'vertical' | 'horizontal';
    label: string;
    /** The panes it sizes, by id. */
    controls?: string;
    /** The pixels the fractions share; the parent's length less one gap where not given. */
    span?: () => number;
    class?: string;
    onchange: (value: number) => void;
  }

  let { value, min = 0.1, max = 0.9, reset, orientation = 'vertical', label, controls = undefined, span = undefined, class: extra = '', onchange }: Props = $props();

  const STEP = 0.02;
  const LEAP = 0.1;

  let node = $state<HTMLElement | undefined>();
  let drag: { from: number; at: number; length: number; pointer: number } | null = $state(null);

  const across = $derived(orientation === 'vertical');
  const percent = (fraction: number) => Math.round(fraction * 100);

  function length(): number {
    if (span) {
      return span();
    }

    const parent = node?.parentElement;

    if (!parent) {
      return 0;
    }

    const box = parent.getBoundingClientRect();
    const gap = parseFloat(getComputedStyle(parent)[across ? 'columnGap' : 'rowGap']);

    return (across ? box.width : box.height) - (Number.isFinite(gap) ? gap : 0);
  }

  function move(to: number) {
    const next = clampSplit(to, min, max);

    if (next !== value) {
      onchange(next);
    }
  }

  /** Terminals sized while it moved tell their sessions now. */
  function settle() {
    document.dispatchEvent(new CustomEvent(SPLIT_SETTLED));
  }

  function down(event: PointerEvent) {
    if (event.button !== 0) {
      return;
    }

    const total = length();

    if (total <= 0) {
      return;
    }

    event.preventDefault();
    node?.setPointerCapture?.(event.pointerId);
    node?.focus({ preventScroll: true });
    drag = { from: across ? event.clientX : event.clientY, at: value, length: total, pointer: event.pointerId };
  }

  function moved(event: PointerEvent) {
    if (!drag || event.pointerId !== drag.pointer) {
      return;
    }

    move(drag.at + ((across ? event.clientX : event.clientY) - drag.from) / drag.length);
  }

  function up(event: PointerEvent) {
    if (!drag || event.pointerId !== drag.pointer) {
      return;
    }

    node?.releasePointerCapture?.(event.pointerId);
    drag = null;
    settle();
  }

  function keys(event: KeyboardEvent) {
    if (event.ctrlKey || event.altKey || event.metaKey) {
      return;
    }

    const back = across ? 'ArrowLeft' : 'ArrowUp';
    const on = across ? 'ArrowRight' : 'ArrowDown';
    const by = event.shiftKey ? LEAP : STEP;
    let to: number | null = null;

    if (event.key === back) {
      to = value - by;
    } else if (event.key === on) {
      to = value + by;
    } else if (event.key === 'Home') {
      to = min;
    } else if (event.key === 'End') {
      to = max;
    } else if (event.key === 'Enter') {
      to = reset;
    }

    if (to === null) {
      return;
    }

    // Kept here, so a page's own arrows and Home and End don't also act.
    event.preventDefault();
    event.stopPropagation();
    move(to);
  }

  function released(event: KeyboardEvent) {
    if (/^(Arrow(Left|Right|Up|Down)|Home|End|Enter)$/.test(event.key)) {
      settle();
    }
  }

  function again() {
    move(reset);
    settle();
  }
</script>

<!-- A separator that takes focus is a control: the keys are its own. -->
<!-- svelte-ignore a11y_no_noninteractive_tabindex, a11y_no_noninteractive_element_interactions -->
<div
  bind:this={node}
  class={['ls-split', orientation, extra].filter(Boolean).join(' ')}
  class:dragging={!!drag}
  role="separator"
  tabindex="0"
  aria-orientation={orientation}
  aria-label={label}
  aria-controls={controls}
  aria-valuenow={percent(value)}
  aria-valuemin={percent(min)}
  aria-valuemax={percent(max)}
  title="Drag, or the arrow keys; Enter or a double click puts it back"
  onpointerdown={down}
  onpointermove={moved}
  onpointerup={up}
  onpointercancel={up}
  onkeydown={keys}
  onkeyup={released}
  ondblclick={again}
>
  <span class="grip" aria-hidden="true"></span>
</div>
