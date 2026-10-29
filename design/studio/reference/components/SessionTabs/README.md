# SessionTabs

SessionTabs is Terminal Focus's strip of sessions: one tab each, with its state as a Signal glyph (the word is its accessible text), its title, project and agent, a ContextMark for how full its context window is, its key (Ctrl+Alt+1 to 9), and a dot when it has written since you last looked at it. Each tab can be closed, renamed and moved, as main's `studio/src/lib/components/SessionTabs.svelte` builds it.

## What it does

- A session that needs you gets a Needs underline as well as its glyph.
- Arrow keys, Home and End move between tabs and show the session. Double-click a tab to show only that one. The + opens the launch sheet.
- **Close** (×, or Delete): an ended tab is cleared from this browser at once. A running one asks "Close …?" with **Hide** ("this browser only · changes nothing anywhere else") or **End session…**, which asks again, naming the project, with `$ loadout session end <id> --yes`. Where Studio is only watching, closing hides.
- **Rename in place** (F2, or the menu): Enter saves, Escape cancels, empty goes back to the launch name. The hint says so, says whether the agent is given the name too ("Its agent is given the name too." or "Only Loadout shows this name: its agent has not said it can take one."), and shows the command it sends, `$ loadout session rename <id> "<name>"` (`--reset` for the launch name).
- **The menu** (right-click, the context-menu key, Shift+F10): Rename (F2), Back to "<launch name>", Move left and Move right (Ctrl Shift ← →), then Hide and End session… for a running one or Clear (Delete) for an ended one. End session… is in the Bad colour, and says End.
- **Drag** a tab along the strip to reorder it; a line shows where it will land.
- **N hidden** lists the hidden tabs; picking one shows it again.
- The strip scrolls sideways inside itself when there are more tabs than room; the page never does.

## What you provide

- `sessions`: id, title, state word, `where`, `agent`, `unread`, `context` (percent or null), `ended`, `launchTitle`, `namesAgent`, `kind` ("launch" or "command"), `project`, and `hid`, the daemon's id for the commands (defaults to id).
- `current`, `controls` (the panes' id), `hidden`, and `changes: false` where Studio is only watching (no Rename, no End).
- Callbacks: `onPick`, `onMaximise`, `onNew`, `onMove(order)`, `onRename(id, name | null)`, `onHide`, `onUnhide`, `onClear`, `onEnd`. Each change is made on the strip at once and told to its callback; `el.update({ sessions, hidden, current, changes })` hands the page's arrangement back, keeping a rename in progress.

## Accessibility

- A tablist of tabs, each with `aria-selected`, `aria-controls`, `aria-keyshortcuts` and `aria-haspopup="menu"`. The rename field is named "New name for …" and described by the hint.
- The menu is a `role="menu"` of menu items: arrows, Home and End move, Escape closes and gives focus back to the tab, Tab closes it.
- Every action has a key, and the close button is a pointer convenience only (the tab itself takes Delete).
