# LineStrip

LineStrip shows the line picked on the map drawn straight across: its badge, title and Signal, its five stations with the train where the work is, its last events as ticks, its facts and checks, and what you can do, each with its command.

## What you provide

- `line`: the same object the TubeMap draws.
- `facts`: `{ k, v }` pairs, mono values ("Agent · Claude Code", "Running · 48 min", "Tokens · in 18,204 · out 6,120").
- `checks`, `events` (newest first, five at most) and `actions` (HaloButtons with commands; things that change nothing say so, "opens here · changes nothing").

## Do and don't

- Do open it under the map when a line is picked, and keep it open as the map changes.
- Don't put more than three actions in it. More belongs in the line's own page.

## Accessibility

- A labelled `<section>` ("Line 4: Release 0.66.0"); the drawing is `aria-hidden` and the events are a real list.
- The strip wraps to stacked columns on narrow screens; the drawing scales with its width.
