# Signal

Signal shows a state as four things at once: a word, a glyph in a ring, the family colour and the family's motion. Every state in Studio is shown this way.

## The five families

| Family | States | Motion (full) | Under reduced or none |
|---|---|---|---|
| Moving | working, running, doing, round running | the ring breathes; a round spins | still |
| Needs | needs a decision, waiting for you | the ring pulses | still |
| Bad | failed, last round failed, blocked, limited | none: a square, heavier ring | the same |
| Good | done, met, accepted | one settle flash | still |
| Rest | idle, open, captured, shaping, held, stopped, not attempted, unknown, dropped | idle dims (in Dark); not attempted is dashed; unknown flickers once, then dashed; dropped is struck through | the same, without the flicker |

## What you provide

- The state word, exactly as Loadout says it. `wordFor(item)` derives it for a session, idea, task or team run, the way the boards do: a quiet session reads idle after `idleAfter` minutes (10, a proposed Studio setting).
- `big: true` for headers and sheets (28px ring, Saira label); the default is the 20px ring with a mono label for lists.

## Do and don't

- Do show the word every time. The glyph and colour repeat it.
- Don't invent states. A word Loadout does not use reads as Rest.
- Don't colour anything else with a family colour unless it carries that state.

## Accessibility

- The glyph ring is `aria-hidden`; the word is the text.
- Done (a check) and failed (a cross, a square ring) differ by glyph, ring shape and word, because `good` and `bad` are close in lightness.
- Loops run only under full motion, and nothing flashes more than three times a second.
