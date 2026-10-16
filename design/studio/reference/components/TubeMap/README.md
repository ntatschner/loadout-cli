# TubeMap

TubeMap is the Map view of Home: every piece of work is a line in its own colour, every stage is a station, and Needs you is the interchange every line passes through, so what is waiting reads as one column.

## The map's grammar

- **Line:** a track in the line's identity colour (`line-1` to `line-8`), solid behind its train and dashed ahead. Identity, never state: a line keeps its colour from Captured to Done.
- **Stop:** a tick at each station; ticks the work has passed are `ink-strong`.
- **Interchange:** Needs you. A ringed circle on every line, joined by one capsule, on the only tinted zone (`needs-wash`).
- **Train:** a numbered disc in the line's colour (`on-line` number) at the station the work is at; it glides along the line, bends included. A waiting train has an amber ring that breathes.
- **Checks as stops:** a team run's checks sit after the interchange: ✓ met, ✕ unmet, ○ not attempted.
- **Branches:** a team run's workers branch off at Running, each with its state written beside it.
- **Signal light:** a blocked line shows a red light with a bar where it stopped.
- **Ticks:** the last five events, newest first, as small marks before their station.

## What you provide

- `lines`: `{ id, code, title, kind, where, stage, waiting, blocked, failed, facts, checks, workers, events }`. Keep `code` ("1" to "8") stable for the life of the work.
- `picked` and `onPick(id)`: show the picked line in a LineStrip below.

## Do and don't

- Do put the NetworkStatus above the map and the LineStrip below it.
- Don't draw more than eight lines. Group them, or show one map per project.
- Don't use the London Underground roundel, the Johnston typeface or TfL's line colours. The map borrows the grammar of a diagram, not anyone's brand.

## Accessibility

- The drawing is `aria-hidden`. Each line board is a button whose name says everything the map shows: "Line 4, team run: Release 0.66.0, running. quiet 1m".
- The map is 1280px wide and scrolls sideways inside its own box; the page never does.
- Trains jump instead of gliding under reduced or no motion, and the waiting ring stops breathing.
