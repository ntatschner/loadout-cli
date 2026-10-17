# CommandPalette

CommandPalette opens on Ctrl K (Cmd K on a Mac) from anywhere: go to a page, find a piece of work in flight, or do something, with every action showing the `loadout` command it runs, so the palette teaches the command line as you use it.

## What you provide

- `groups`: "Go to" (pages), "In flight" (work, with `need: true` for anything waiting for you) and "Do" (actions). Each row: `glyph`, `label`, `sub` (where it is and its state, or the command), `kind`, `run(row)`.
- Actions that need more input say "opens …" and open their Dialog; actions that cannot be undone say "asks first".

## Ranking

- Words typed must all appear. Rows whose label matches rank above rows that match only in their facts, and a group whose best row matches its label moves up.

## Do and don't

- Do write the command in `sub` for every action that changes something.
- Don't list reading a secret back or terminal-only agreements as actions; there is nothing for the palette to run.

## Accessibility

- A modal dialog holding a `combobox` that controls a `listbox`, with `aria-activedescendant`; groups are labelled.
- Arrow keys move, Enter runs, Escape closes and returns focus. The search row in the SideRail opens it too.
