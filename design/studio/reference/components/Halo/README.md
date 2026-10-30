# Halo

Halo is the centre of the Lens: the piece of work in focus, drawn as light around its words. Orbit is five stage rings with a body circling the ring the work is on; Gyroscope is three tilted rings for its stage, the hour and its spend.

## Choosing a style

- Both are usable. Orbit is the default. The person chooses per component in Settings → Appearance, and the choice is kept in Loadout's config (proposed key `studio-halo`).
- Orbit scales down with its container. Gyroscope also shows the hour and spend, and its rings are drawn at a fixed 540px, so give it that much room.

## What you provide

- `stage`: 0 Captured, 1 Shaping, 2 Running, 3 Needs you, 4 Done.
- `core`: the words in the middle. The kind and where (`cmd` style), the title (`title`), its Signal (big), a note, and when it is waiting, the question and its answers as HaloButtons with commands.
- Gyroscope: `hour` (0 to 1, how far through the hour it has run) and `spend` (0 to 1 against its cap, or `null` when there is no cap; the ring is then empty and the words say "no cap").
- `orbiters` (Ambient): the other items, each a button that brings it into the halo.

## The rings

| Ring | Colour | Meaning |
|---|---|---|
| Stage rings (Orbit), stage arcs (Gyroscope) | `rest`, `shaping`, `moving`, `needs`, `good` | the stage reached; the current one is lit and glows |
| Hour ring (Gyroscope) | `accent` | how far through the hour |
| Spend ring (Gyroscope) | `gauge` | spend against its cap, always labelled "Spend" in the legend |

## Do and don't

- Do put the answers in the core when the work needs you; the halo is where you answer it.
- Do redraw it as often as the work changes: every spin is timed from one clock, so a new halo carries on where the last one was, and a gyroscope redrawn under the pointer keeps its tilt.
- Don't show the halo without its words. The rings repeat the Signal; they never replace it.

## Accessibility

- The drawing is `aria-hidden`; a hidden line says "Stage 3 of 5: Running", and the core's text says the rest.
- The orbit spins and the gyroscope turns only under full motion; reduced motion holds them still, and no motion also flattens the gyroscope's tilt.
- Orbiters pause while the pointer or focus is on the halo, so they can be pressed.
