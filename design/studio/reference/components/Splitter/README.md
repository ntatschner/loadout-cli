# Splitter

Splitter is the divider between two panes of a split, for every split Studio has (the terminal deck, WorkDesk's list and actions, the page beside), so they all drag and take keys the same way. It sits in the gap of its grid, placed there by the page's own CSS, and only says where the boundary should be: the page turns that into its tracks. The arrows move it a little, Shift with an arrow further, Home and End to its limits, and Enter or a double click puts it back where it started. Ported from main's `studio/src/lib/components/Splitter.svelte` as built.

## What it shows

- A 12px hit area over the gap, with a short handle (4 × 28px) at rest so it can be found.
- On hover or while dragged, the full line and handle in the accent; with keyboard focus, in the focus colour with a ring.
- Col-resize or row-resize cursor.

## What you provide

- `value`, `min`, `max` and `reset` as fractions of the length the panes share; `orientation`; `label`; `controls` (the panes' id); `onchange(value)`.
- `span()` when the shared length is not the parent's length less one gap.
- `class` to place it: the parent is `position: relative` and a grid; put it in the second track (`grid-column: 2`, or `grid-row: 2` for horizontal) and set `--split-gap` to the grid's gap. Turn the fraction into tracks with `Splitter.tracks(value)`.
- Listen for `ls-split-settled` on the document to tell terminals their new size once it is let go or its key released.
- `set({ value, min, max })` when the page holds it somewhere else (a floor, a window resize).

## Do and don't

- Do keep the fraction, not pixels, so a split keeps its proportions as the window changes.
- Do hide it where the layout stacks (WorkDesk under 1024px, the page beside under 1100px).
- Don't give a split its own keys. Every split takes these.
- Its class name, `ls-split`, is TokenSplit's too. Its rules are held to `.ls-split:where([role="separator"])`, so TokenSplit's figures stay in the page flow; a page rule that places or hides a divider can still say `.ls-desk > .ls-split`, since `:where()` adds no weight. (Main's components.css has the clash: there TokenSplit is laid out as a divider.)

## Accessibility

- `role="separator"`, focusable, with `aria-orientation`, `aria-label`, `aria-controls` and `aria-valuenow`/`min`/`max` as whole percents.
- ←/→ (or ↑/↓) 2%, Shift 10%, Home/End to the limits, Enter back to its start. The keys stop there, so a page's own arrows don't also act. Ctrl, Alt and Meta pass through.
- Its title says how: "Drag, or the arrow keys; Enter or a double click puts it back".
