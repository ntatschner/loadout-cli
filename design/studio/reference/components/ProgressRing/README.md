# ProgressRing

ProgressRing is a ring that fills for stages, the Meter's round form: a thin track, an arc in Moving that fills clockwise from the top, and the percentage in the middle. It is for a short run of stages (an idea from captured to done), not for money or tokens, which have the Meter and the Spend line. Ported from main's `studio/src/lib/components/ProgressRing.svelte` as built.

## What it shows

- **The track**, faint Moving (the control edge in the high-contrast themes).
- **The arc**, Moving, the share done. It eases to a new value over a second under full motion and jumps under reduced motion or none.
- **The percentage**, in mono, in the middle.

## What you provide

- `value`: from 0 to 1. It is clamped, so 1.2 draws as full and -0.1 as empty.
- `set(next)` moves it: a number, or `{ value }`.

## Do and don't

- Do say the stage in words beside it ("Stage 3 of 5 · running"). The ring and its figure are decoration.
- Do keep it for stages that move forward. A figure that can go past a limit is a Meter.
- Don't colour the ring by state: it is always Moving. A run that needs you says so with its Signal.

## Accessibility

- The ring and the percentage are `aria-hidden`: the words beside them carry the stage.
- Arc and track differ by more than colour in every theme: the arc is wider and solid.
- Motion follows `data-motion` and `prefers-reduced-motion`.
