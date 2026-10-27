# SplitCross

SplitCross sits where a column divider and a row divider cross, in the terminal deck's four panes: dragging here moves both, and a double click puts both back. It is for the pointer only; the two dividers either side of it already take the keys, so it takes no focus and a screen reader is not told about it twice. Ported from main's `studio/src/lib/components/SplitCross.svelte` as built.

## What it shows

- A 20px square over the crossing, with an 8px outlined square in the control edge colour; filled with the accent on hover or while dragged. Move cursor.

## What you provide

- `column`, `row` and their limits (`colMin`, `colMax`, `rowMin`, `rowMax`), `reset` as `[column, row]`, and `onchange(column, row)`.
- Place it in the second column and row of the same grid as the two Splitters (`grid-column: 2; grid-row: 2`), after them. Keep the three in step: a Splitter's change goes to `cross.set({ column })`, the cross's change to both Splitters' `set({ value })`.
- It raises `ls-split-settled` on the document when let go or double clicked, as Splitter does.

## Do and don't

- Do pair it with two Splitters; alone it leaves the keyboard nothing.
- Don't make it focusable.

## Accessibility

- `aria-hidden="true"` and no tabindex: the two Splitters are the accessible controls.
- Its title says how: "Drag to move both dividers; a double click puts them back".
