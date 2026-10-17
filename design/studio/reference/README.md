Loadout Studio is the design system for Studio: the app Loadout serves on this machine, installable as a PWA, where you start sessions, watch them run, and steer ideas, tasks, team runs, memory and spend. It is separate from Loadout Kit, which stays the system for the classic dashboard. Studio's look is an instrument panel lit from inside: dark ground, thin type, rings of light that breathe while work moves. Build every Studio screen from these tokens and classes; the final app is written in Svelte, so components are plain CSS classes and small helpers that port line for line.

## Principles

1. **Work moves, and you can see it move.** Things travel between stages, rings breathe while something works, and the thing that most needs you rises while the rest recede.
2. **Answer where you see it.** Anything waiting for you can be answered where it is shown. You never have to open a detail view to unblock something.
3. **Every action shows its command.** Anything that changes something shows the exact `loadout` command it runs, in `cmd` style, beside or under the control (`$ loadout team gate --answer Approve`). Two things never get a button, only the command to run in a terminal: reading a secret back, and agreements that must be made in a terminal (`$ loadout tools verify <draft> --agree`).
4. **A state is a word, a glyph, a colour and a motion.** All four, every time. Colour repeats the state; it never carries it alone.
5. **Four themes, all measured.** Dark, Light, High contrast dark and High contrast light are designed and checked separately. None is a filter over another.

## Content fundamentals

- Use the command line's words: sessions, ideas, tasks, team runs, rounds, gates. Say "waiting for you" and "needs a decision", not "pending" or "action required".
- Address the reader as *you*. Name work by what it is for ("Retry the upload with backoff"), never by an id alone.
- Join short facts with a middle dot: "line 4 · team run · release-crew".
- State words are lower case and come from Loadout: working, idle, running, done, failed, stopped, limited, blocked, needs a decision. Stage names are mixed case: Captured, Shaping, Running, Needs you, Done.
- Labels, buttons and zone headings are set in spaced capitals by their style (`label-*`); write them in sentence case in the source ("Leave ambient"), never in capitals.
- Say what something costs in tokens. Spend thresholds say where you stand; they never stop anything, so never word them as a limit.
- No emoji, no exclamation marks.

## Visual foundations

### Themes

- Set `data-theme` on `<html>`: `dark` (the default), `light`, `hc-dark`, `hc-light`. With no saved choice, use `LoadoutStudio.preferredTheme()`, which follows the operating system.
- **Dark** is the boards' look: `ground` #03070c, `ink` #d9f3ff, and light from the accent. Its values are the chosen boards' exact values.
- **Light** is a daylight instrument: white-blue paper (`ground` #f3f7fa, `surface` white), crisp dark ink, and the accent as solid strokes. There is no glow; shapes and motion stay the same as Dark.
- **High contrast dark** and **High contrast light** meet WCAG AAA: every text token is 7:1 or better on `ground`, `surface` and `surface-2`; surfaces are set apart by `rule` borders at 3:1, not by shade; no glow; focus is yellow (`hc-dark`) or blue (`hc-light`).

### Colour

- Put text in `ink` on `ground`, `surface` or `surface-2`. Use `ink-strong` for what must read first (titles, numbers), `ink-soft` for zone headings, `muted` for secondary facts, and `subtle` for commands, placeholders and times.
- Every text token is 4.5:1 or better on all three grounds in Dark and Light, and 7:1 or better in both high-contrast themes. The tightest pair is 5.6:1 in Light (`good` on `surface-2`).
- Raise panels with `surface` (dialogs, popovers, toasts, the palette, the widened rail) and `surface-2` (a hovered row, a code well, a meter track). Dim the page behind a dialog with `scrim`.
- Draw grouping hairlines in `rule`, or in the accent mixed at `tint-line` for the line after a zone heading. Draw the edge of an unfilled control in `control-edge` (3:1 or better everywhere).
- **Accent.** Read `accent` for rings, strokes and fills, `accent-text` for accent words, `on-accent` for text on an accent fill. The person picks Cyan (default), Ice, Magenta or Violet with `data-accent`; the named tokens (`accent-cyan`, `accent-cyan-text`, `on-accent-cyan` and so on) exist only to be rebound. The accent never means a state.
- **Signals.** Five families carry every state, each as a word, a glyph, a colour and a motion:

| Family | Token | States | Motion (full) |
|---|---|---|---|
| Moving | `moving` | working, running, doing, round running | breathe; spin for a round |
| Needs | `needs`, `needs-text`, `needs-wash` | needs a decision, waiting for you | pulse, then a glow that stays |
| Bad | `bad`, `bad-text`, `bad-wash` | failed, last round failed, blocked, limited | none: a hard edge |
| Good | `good` | done, met, accepted | one settle flash, then still |
| Rest | `rest` | idle, open, captured, shaping, held, stopped, not attempted, unknown, dropped | dim (Dark only), dashed or struck through; never a loop |

- `good` and `bad` are close in lightness, so they are always told apart by glyph and word (a check against a cross), never by colour alone. `moving` sits near the Cyan accent for the same reason: a Moving thing always shows its play glyph and its word.
- Fill washes (`needs-wash`, `bad-wash`) only ever sit under their text colour and a 3px edge in the family colour.
- `gauge` is the Gyroscope halo's spend ring. It is always labelled "Spend".
- Put text on a solid signal fill in `on-signal`.
- `shaping` colours the Lens halo's Shaping ring only. As a state, shaping belongs to the Rest family.
- The Map gives each piece of work a line colour, `line-1` to `line-8`, for its whole life. Line colours are identity, never state, and its number on a train or badge is `on-line`. They appear only inside the Map parts (TubeMap, LineStrip, NetworkStatus).

### Light and glow

- Glow is light from the accent or a signal colour: `0 0 var(--glow-md) -2px var(--glow)` round a ring, `var(--glow-sm)` on a dot, `var(--glow-lg)` on hover and focus.
- `--glow` is derived in `bundle.css` from `accent` at `glow-strength`. It is on in Dark and transparent in Light and both high-contrast themes, so write glows once and let the theme switch them off. Never use glow as the only difference between two states.
- Tint a hovered row with `--tint-hover-c` and a picked row with a fade from `--tint-picked-c`, plus a 3px `accent` edge.

### Type

- Saira (variable, 100–900) for everything you read; JetBrains Mono (variable, 100–800) for commands, times and counts. Both are bundled as self-hosted woff2 under `fonts/`, with their OFL licences under `licenses/`. Studio fetches nothing.
- Display is thin on purpose: `display-xl` 40/200, `display` 30/200, `title` 21/300, `ask` 19/400. In Dark the glow gives thin type its weight; in Light and high contrast the ink does.
- Text: `entry` 20/300 for the start-a-session rail, `body` 15/400, `body-sm` 13.5/400, `small` 12.5/400.
- Labels are spaced capitals at weight 500: `label-zone` (.24em), `label-button` and `label-button-sm` (.22em), `label-tab` (.2em), `label-nav` (.24em).
- Mono: `cmd` 11.5 for every command, `mono-meta` 12 for times, `mono-note` 11 for live counts.
- Never set body text below `small` (12.5px). Mono notes may go to 11px because they repeat what the words already say.

### Space and density

- Use the spacing scale `space-1` (4px) to `space-8` (40px), always multiplied by density: `calc(var(--space-4) * var(--density))`.
- `data-density` picks Compact (0.8), Comfortable (1, the default) or Roomy (1.25). `rail-width`, `rail-width-open` and the control heights are not multiplied, and no control is ever below 24px.
- Controls are `control` (44px) tall, small ones `control-sm` (36px), the start rail `control-lg` (60px).
- Pages start `rail-width` (84px) in, beside the side rail. Below 600px the rail hides behind a Pages tab and the page runs to a 16px (`space-4`) gutter. Nothing ever scrolls sideways at 320px.

### Shape

- Studio is square: panels, rails, toasts, tabs and the start rail use `radius-none`. Rings, glyph discs and orbiters use `radius-round`; meter tracks use `radius-pill`; signal marks use `radius-chip`.
- Edges are drawn as inset box-shadows or 1px lines, never as rounded cards.

### Motion

- Use `ease-out` for nearly everything and `ease-travel` for things that follow a path. Durations run from `dur-instant` (180ms) to `dur-travel` (900ms); a Moving glyph breathes over `dur-breathe`; a toast folds after `dur-toast-fold`.
- Motion is `full`, `reduced` or `none` (`data-motion`), and the operating system's reduced-motion preference always wins: read the level in force with `LoadoutStudio.motion()`. Under reduced, nothing loops and things fade instead of travelling; under none, nothing animates.
- Nothing flashes more than three times a second.

### Focus and states

- The focus ring is a solid 2px `focus` outline, 2–4px off the control, 3:1 or better on every ground. On a halo button it rings the halo, not the label.
- Hover grows a halo ring and lights its centre; press fills it. Disabled controls say why in words beside them rather than greying out.
- What is less urgent recedes to `recede` (0.72) when something needs you, in Dark only, where text stays 4.5:1 or better. In Light and both high-contrast themes nothing fades, because a fade would take text under its contrast floor; the rising item's edge and light carry it alone.

## Iconography

- Signal glyphs are inline SVG on a 20×20 grid, drawn at 12px (16px in the big form) with a 2 stroke and round caps, inside a ring in the family colour: play (working), a rotating arrow (round running), an exclamation (needs), a cross (failed), a barred circle (blocked), a gauge needle (limited), a check (done), a dash (idle), an open circle (open), a diamond (shaping), two bars (held), a square (stopped), a question mark (unknown) and a slash (dropped). They always sit beside their word.
- Page icons in the side rail are 20×20 line icons with a 1.6 stroke, in `ink`, and `accent` for the current page.
- The halo ring (a 1.5px ring in `accent` with a dot at its centre) is Studio's button. It is not an icon and never appears without its label.
- No emoji. No logo yet: Studio's name is set in Saira until its mark is designed.
