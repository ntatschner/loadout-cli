# CommandPalette

CommandPalette opens on Ctrl K (Cmd K on a Mac) from anywhere: go to a page, find a piece of work in flight, or do something, with every action showing the `loadout` command it runs, so the palette teaches the command line as you use it.

## What you provide

- `groups`: "Go to" (pages), "In flight" (work, with `need: true` for anything waiting for you) and "Do" (actions). Each row: `glyph`, `label`, `sub` (where it is and its state, or the command), `kind`, `run(row)`.
- Actions that need more input say "opens …" and open their Dialog; actions that cannot be undone say "asks first". A row with `stay: true` keeps the palette open when run.
- `search(query)` (optional): looks for what is typed everywhere, once it is two characters long and typing has paused for 250ms, and resolves to more groups ("Found in tasks"), shown after the palette's own rows. An answer to an older query is dropped.
- `ask` (optional): `{ asker, approve, agent, conversation }` for asking Loadout. `asker(query)` is `loadout ask --query=… --json`; `agent(query, resume)` asks the agent instead (a job that spends a turn), carrying on its conversation; `approve(plan, team, cap)` carries out a plan. `conversation` is an AskPane to keep the conversation in; without one the palette keeps its own for as long as it lives.
- `onHighlight(row, el, group)` (optional): the highlighted row as it moves, by arrow, pointer or typing, with its element and group name; `null` when nothing is highlighted or the palette closes. It is called only on a change, so a page can preview the row beside the palette (In flight peeks there). A row's `key` is on its element as `data-key`.

## Asking Loadout

- With `ask`, the box says "Go to a page, find work, or do something, or ? to ask". Typing a leading `?`, or choosing the **Ask Loadout: "…"** row offered after the palette's own rows, turns the results into the conversation (AskPane), and Enter asks. The box is left as `? ` for the next request.
- The foot says "Enter to ask" and "Esc to close, and the conversation is kept": opening the palette again brings it back.
- Once a plan is approved, focus comes back to the box, since Approve is held from then on. A refusal leaves focus on Approve, to press again.

## Ranking

- Words typed must all appear. Rows whose label matches rank above rows that match only in their facts, and a group whose best row matches its label moves up.

## Do and don't

- Do write the command in `sub` for every action that changes something.
- Don't list reading a secret back or terminal-only agreements as actions; there is nothing for the palette to run.

## Accessibility

- A modal dialog holding a `combobox` that controls a `listbox`, with `aria-activedescendant`; groups are labelled.
- Arrow keys move, Enter runs, Escape closes and returns focus. The search row in the SideRail opens it too.
- While asking, the combobox says it is collapsed (`aria-expanded="false"`) and the listbox is hidden; the conversation is a polite live region.
