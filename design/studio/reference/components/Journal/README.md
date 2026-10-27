# Journal

Journal is a run's journal or a node's stream, one line each: when, what kind, what was said, and how many times a repeat came round. Main draws it in the first depth of a run's page, "What it said": the run's own journal, or, with a node picked, everything that node did, its sub-agents indented. Ported from main's `studio/src/lib/components/Journal.svelte` as built.

## What it shows

- **The time**, in mono, in a narrow column.
- **The kind** ("round", "gate", "said", "bash"), small, before the words.
- **The words**, in Needs, Bad or Good when the line's family says so.
- **Repeats** as "×4" after the words, so a quiet run is one line, not forty.
- **Sub-agent lines** indented under the node that started them.

## What you provide

- `lines`: `{ time, text, family?, kind?, sub?, repeats? }` each, as main's `JournalLine`.
- `label` for a node's stream ("What implementer/1 did"); the run's own is "Journal".
- `set({ lines, label })` as the stream moves.

## Do and don't

- Do put it where main does, inside RunDepths (`.ls-depths`): its look is that panel's, a scrolling box 360px high at most.
- Do say where it comes from under it: `$ loadout team log <run> --events`.
- Do give an empty stream one line ("Nothing from verifier/1 yet.") rather than an empty box.
- Don't colour a line without a kind word: the word says what the colour does.

## Accessibility

- An ordered list with its own name, focusable (`tabindex="0"`) so it can be scrolled from the keyboard.
- Colour is never alone: each coloured line has its kind word ("refused", "gate").
