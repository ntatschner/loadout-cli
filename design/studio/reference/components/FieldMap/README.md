# FieldMap

FieldMap is Studio's background: an **embossed military survey map**. It has contour lines, with every fifth heavier as an index contour, faint dashed grid squares every 200px, and map references: A, B, C… along the foot, and 01, 02… down the left edge, where they show through the rail and never sit beside page content. It is generated from a fixed seed, so it is the same map on every visit and on every machine. It is decoration: `aria-hidden`, and it never takes a pointer.

## Embossed, not drawn

Each contour is drawn three times, all in the theme's own colours:

- a light edge, offset up and left;
- a dark edge, offset down and right;
- the line itself, in the accent (4% in Dark, 5.5% in Light; index contours 7% and 9%).

So the relief reads as stamped into the ground, not printed on it.

## Settings

| `data-background` | What shows |
|---|---|
| `plain` | today's ground |
| `map` (default) | the field map, still |
| `map-animated` | the map with a slow scan line (16s, top to bottom) that lights the contours it crosses (in the accent in Dark, a white glint in Light), and the index contours breathing over 9s |

Chosen in Appearance → **Background**, and stored as `studio-background`. The animated map only moves under **full** motion; reduced, none, the system's reduced-motion setting and **Pure Focus** all get the still map. Both high-contrast themes always get the plain ground.

## Readable on top

- Measured, not assumed: on the map's worst pixel (an index contour with its edge, a grid line and the scan together, or a map reference over a grid line), every text colour stays 4.5:1 or better, in both themes and all four accents. The lowest is 4.62:1. If you change a token or a level, measure again.
- In Light the scan brightens the lines instead of darkening them, because any extra darkening takes accent text under 4.5:1.
- Panels, cards, dialogs and the terminal stay solid, as they are.

## The nav

The map carries on under the side rail and the mode bar:

- **The rail:** its background becomes 58% ground with a 1.5px blur, so the map shows through, dimmer. When it widens over the page (hover or focus) it is solid ground again, with its usual open edge, so page text never shows under its labels. Its edge is embossed (a dark and a light line), with grid-reference ticks every 40px and fine ticks every 8px.
- **The mode bar:** the same embossed edge and ticks along its foot.

## Cost

One SVG, drawn once and redrawn 250ms after a resize. Each set of contours is a single path in `defs`, reused by `<use>` for the dark edge, the light edge, the line and the scan, so the page holds its path data once. The scan is a CSS mask moving over a second copy of the lines, so nothing is recomputed while it runs.
