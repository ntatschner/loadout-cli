<script lang="ts">
  // Where a column divider and a row divider cross: dragging here moves
  // both, and a double click puts both back. It is for the pointer only;
  // the two dividers either side of it already take the keys, so it takes
  // no focus and a screen reader is not told about it twice.
  import { clampSplit, SPLIT_SETTLED } from '../splits';

  interface Props {
    /** Where the column and the row boundaries sit, as fractions of the lengths the panes share. */
    column: number;
    row: number;
    colMin?: number;
    colMax?: number;
    rowMin?: number;
    rowMax?: number;
    reset: [number, number];
    onchange: (column: number, row: number) => void;
  }

  let { column, row, colMin = 0.1, colMax = 0.9, rowMin = 0.1, rowMax = 0.9, reset, onchange }: Props = $props();

  let node = $state<HTMLElement | undefined>();
  let drag: { x: number; y: number; column: number; row: number; wide: number; tall: number; pointer: number } | null = $state(null);

  /** The parent's width and height, less one gap each way. */
  function lengths(): [number, number] {
    const parent = node?.parentElement;

    if (!parent) {
      return [0, 0];
    }

    const box = parent.getBoundingClientRect();
    const style = getComputedStyle(parent);
    const gap = (value: string) => (Number.isFinite(parseFloat(value)) ? parseFloat(value) : 0);

    return [box.width - gap(style.columnGap), box.height - gap(style.rowGap)];
  }

  function down(event: PointerEvent) {
    const [wide, tall] = lengths();

    if (event.button !== 0 || wide <= 0 || tall <= 0) {
      return;
    }

    event.preventDefault();
    node?.setPointerCapture?.(event.pointerId);
    drag = { x: event.clientX, y: event.clientY, column, row, wide, tall, pointer: event.pointerId };
  }

  function moved(event: PointerEvent) {
    if (!drag || event.pointerId !== drag.pointer) {
      return;
    }

    const toColumn = clampSplit(drag.column + (event.clientX - drag.x) / drag.wide, colMin, colMax);
    const toRow = clampSplit(drag.row + (event.clientY - drag.y) / drag.tall, rowMin, rowMax);

    if (toColumn !== column || toRow !== row) {
      onchange(toColumn, toRow);
    }
  }

  function up(event: PointerEvent) {
    if (!drag || event.pointerId !== drag.pointer) {
      return;
    }

    node?.releasePointerCapture?.(event.pointerId);
    drag = null;
    document.dispatchEvent(new CustomEvent(SPLIT_SETTLED));
  }

  function again() {
    onchange(clampSplit(reset[0], colMin, colMax), clampSplit(reset[1], rowMin, rowMax));
    document.dispatchEvent(new CustomEvent(SPLIT_SETTLED));
  }
</script>

<div
  bind:this={node}
  class="ls-cross"
  class:dragging={!!drag}
  aria-hidden="true"
  title="Drag to move both dividers; a double click puts them back"
  onpointerdown={down}
  onpointermove={moved}
  onpointerup={up}
  onpointercancel={up}
  ondblclick={again}
></div>
