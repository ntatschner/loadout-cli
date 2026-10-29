# FieldMap

FieldMap is the field map Studio can show as its ground: an **embossed military survey map**. It has contour lines, with every fifth heavier as an index contour, faint dashed grid squares every 200px, and map references: A, B, C… along the foot, and 01, 02… down the left edge, where they show through the rail and never sit beside page content. It is generated from a fixed seed (1789), so it is the same map on every visit and on every machine. It carries no data. It is decoration: `aria-hidden`, it never takes a pointer, and nothing in it can take focus. It is opt-in: Studio's default is the plain ground. Built as main's `studio/src/lib/components/FieldMap.svelte`, `lib/fieldmap.ts` and `lib/backdrop.ts`.

## Embossed, not drawn

Each contour is drawn three times, all in the theme's own colours:

- a light edge, offset up and left;
- a dark edge, offset down and right;
- the line itself, in the accent (4% in Dark, 5.5% in Light; index contours 7% in both).

So the relief reads as stamped into the ground, not printed on it.

## Settings

The setting is `data-backdrop` (`setBackdrop()`), stored as `studio-backdrop` and chosen in Appearance → **Background**, after Motion. The command is `loadout config set studio-backdrop <value>`.

| `data-backdrop` | In Appearance | What it asks for |
|---|---|---|
| `plain` (the default, and what unset or unknown means) | Plain | today's ground |
| `map` | Field map, still | the field map, still |
| `map-animated` | Field map, animated | the map with one slow scan line that sweeps the page top to bottom every 24 seconds and lights the contours it crosses, and the index contours breathing over 9s |

Under the group, one line: "The animated map moves only under full motion and pauses while anything waits for you. Pure Focus, Ambient and high contrast keep the plain ground."

## One place decides what shows

`resolveBackdrop()` takes the setting, the theme, the mode, whether Ambient is on, the motion in force (`motion()`, so the system's reduced motion wins), how many things wait for you (the ModeBar's count), whether the tab is hidden and whether a terminal has the keyboard. It returns `plain`, `map` or `map-scan`, in this order:

| When | Shows |
|---|---|
| either high-contrast theme | `plain` |
| Pure Focus | `plain` |
| Ambient is on | `plain` |
| the setting is `plain`, unset or unknown | `plain` |
| the setting is `map` | `map` |
| `map-animated`, under full motion, nothing waiting, the tab visible and no terminal focused | `map-scan` |
| `map-animated` otherwise | `map` |

Write the answer with `setFieldmap()` as `data-fieldmap` on `<html>`, and follow each input as it changes. All the CSS keys off that one attribute: the map is hidden under `plain`, and the scan and the breathing run only under `map-scan`. So the scan stops the moment anything needs you and comes back when the count returns to 0. A `prefers-reduced-motion: reduce` rule stops both as a second guard, and print shows the plain ground.

## What you provide

- `FieldMap({ shown })`, mounted once, as the first thing in the page. `shown` is `resolveBackdrop()`'s answer; `set(shown)` changes it. Leave it out and FieldMap follows `data-fieldmap` on `<html>`.
- `seed`, `cell`, `scale`, `levels` and `square` shape the map; `host`, `width` and `height` place it somewhere other than the whole viewport (for a preview).

## Readable on top

- Measured, not assumed: on the map's worst pixel (an index contour, one of its edges, a grid line and the scan together, or a map reference over a grid line), every text colour (`ink`, `ink-soft`, `muted`, `needs-text`, `bad-text`, `good`, `accent-text`) stays 4.5:1 or better, in Dark and Light and all four accents. The lowest are 5.60:1 in Dark (`muted` with Ice, over an index contour, its light edge, a grid line and the scan) and 4.52:1 in Light (`good` with Cyan, over a reference and a grid line). If you change a token or a level, measure again the same way: composite the layers over `ground` with their alphas in sRGB, then take the WCAG ratio.
- The scan is a neutral tint of the ground, never the accent: `ink-strong` 10% in Dark; in Light a glint of white, because any extra darkening takes text under 4.5:1.

| Layer | Dark | Light |
|---|---|---|
| light edge | `ink-strong` 4% | white 85% |
| dark edge | black 30% | `ink-strong` 4.5% |
| contour | `accent` 4% | `accent` 5.5% |
| index contour | `accent` 7% | `accent` 7% |
| grid | `ink-soft` 4% | `ink-soft` 4.5% |
| map references | `ink-soft` 14% | `ink-soft` 12% |
| scan | `ink-strong` 10% | white 92% |

- Panels, cards, dialogs, popovers, toasts and the palette keep their own surfaces, and **a terminal keeps its own ground** in every mode and theme: the map never shows through it. The map shows only where the page shows its ground, so nothing between `body` and the page may paint an opaque background.

## The nav

Where the map shows (Dark and Light, `map` or `map-scan`), it carries on under the side rail, the Pages tab and the mode bar:

- **The rail:** 58% ground with a 1.5px blur, so the map shows through, dimmer, with an embossed edge (a dark and a light line) and grid-reference ticks: a long one every 40px, a fine one every 8px. When it widens over the page (hover or focus) it is solid ground again, with its usual open edge and scrim, so page text never shows under its labels.
- **The Pages tab** (below 600px): the same embossed edge and ticks. The slide-in list stays solid.
- **The mode bar:** the same embossed edge and ticks along its foot.
- **The scan never crosses the rail or the Pages tab.** It covers the page area only, from the rail's collapsed edge (76px, or 34px where the rail is the Pages tab).

## Do and don't

- Do decide what shows only through `resolveBackdrop()`, and write it only as `data-fieldmap`.
- Do keep the ground on `body` and the terminal's own ground opaque.
- Don't put data on the map, and don't colour the scan in the accent.
- Don't redraw it on a change of setting, theme or mode: the CSS does all of that.

## Accessibility

Decoration only: `aria-hidden="true"`, `pointer-events: none`, nothing focusable. Motion follows `motion()` and the system's reduced motion, and stops while anything waits for you. Both high-contrast themes always get the plain ground.

## Cost

One SVG, drawn after the first paint (when the browser is idle, or a frame later) and only the first time the map is wanted, then again 250ms after the last resize. Each set of contours is a single path in `defs`, reused by `<use>` for the dark edge, the light edge, the line and the scan, so the page holds its path data once. The scan is a CSS mask moving over a second copy of the lines, so nothing is recomputed while it runs.
