# ModeSwitcher

ModeSwitcher picks how the whole of Studio is laid out. Four modes, each a ring-drawn glyph and a word, and a light that travels to the one you pick and flares as it lands.

| Mode | What it is for |
|---|---|
| **Default** | Studio as it is: Home (Lens and Map), Sessions, Teams and every other page |
| **Terminals** (Terminal Focus) | Sessions as tabs over one, two or four big terminals (TermDeck) |
| **Teams** (Team Focus) | Every live run as a line on one board, each run and team opening beside it (RunBoard) |
| **Minimal** | One quiet column of everything live (QuietList); open one and it is all there is (FocusFrame) |

## Behaviour

- A radiogroup: arrow keys, Home and End move and pick. The app also binds **Ctrl+Shift+M** to cycle the modes, and lists each mode in the palette ("Mode: Terminals").
- The mode is set as `data-mode` on `<html>` and kept **in this browser** (local storage), so a phone can stay Minimal while a desktop stays in Terminals. It is not a Loadout config key.
- Picking a mode runs `morph()`: the page sinks and the new layout rises (a view transition, with the rail and the mode bar travelling on their own). Under reduced motion it fades; under none it just changes.
- `compact` hides the words (they stay as the accessible name); `vertical` stacks it for a rail.

## Accessibility

- `role="radiogroup"` named "Mode"; each mode is a radio with its description as its title. In high contrast the picked one is also underlined, since the light's glow is off.
