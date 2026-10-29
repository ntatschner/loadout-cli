# TermDeck

TermDeck is Terminal Focus: the session tabs, a pane layout, and the terminals themselves as big as the screen allows. The side rail stays folded to icons and the page gives the deck its full height. It works as main's `studio/src/lib/components/TermDeck.svelte` and `pages/TerminalFocus.svelte` build it.

## What it does

- **Tabs** (SessionTabs) over **one, two side by side, or four** panes. Each tab closes, renames, moves and hides as SessionTabs says; "N hidden" brings a hidden tab back. A tab's ContextMark shows how full its context window is.
- **Panes keep their places**: focusing one only moves the accent ring, and a session picked from its tab takes the highlighted pane without moving any other. A session that ends stays in its pane until something else is picked there; one gone from the list altogether gets a note saying so. A tab hidden or cleared off the strip gives its pane to the next tab along.
- **A launch takes an empty pane.** A session just started (`launch`) takes an empty pane, growing the layout 1→2→4 to make one; with four full it takes the pane of one that has ended, or else the one idle longest that is not waiting on you. Beside Find it says once where it went: "Opened in pane 3, added for it." or "…in place of ‘…’, which had been idle longest. It is still a tab." Panes grown that way are given back as their sessions go, never below the layout you chose.
- **Dividers** (Splitter, and SplitCross where four panes meet) between the panes, kept per layout: one place for two panes, a column and a row for four. They stop where a terminal would fall below 40 columns by 8 rows, and the tracks follow as `--deck-cols` and `--deck-rows` on the panes.
- **Keys**: Ctrl+Alt+1 to 9 picks a session (read from the physical key, since Ctrl+Alt is AltGr on some layouts), Ctrl+Alt+arrows move between panes, Ctrl+] in a terminal goes back to its tab. Typing goes to the focused terminal only.
- **Go to the one waiting** appears when a session needs you.
- **Find in this terminal** searches the focused terminal: Enter for the next match, Shift Enter for the one before, Escape clears; it says "found" or "no match". A Terminal with `find()` of its own is asked (xterm's search add-on, themed from `needs`); the reference one is searched in place.

## What you provide

- `sessions` (as SessionTabs takes them), `terminal(id)`, which returns a Terminal for that session (the deck never makes connections itself), and `layout`.
- `hidden`, and `changes: false` where Studio is only watching.
- What this browser keeps: `layout` (handed back through `onLayout`), `grown` (`onGrown`), `slots`, the session in each pane (`onSlots`), and `splits` (`onSplit(2 | 4, fractions)`); and the tabs' order, hidden and cleared ones (`onMove`, `onHide`, `onUnhide`, `onClear`). Each change is made in the deck at once and told to its callback, so a page that keeps them can hand them back with `update()`.
- `launch` (or `update({ launch })`, or `launch(id)`) for a session just started, and `active`, when each last wrote or was typed into, for which pane gives way. `onLaunched(id)` says it was placed.
- `onRename(id, name | null)` and `onEnd(id)` for what goes to the daemon (`$ loadout session rename …`, `$ loadout session end … --yes`).
- `listed: false` until the sessions have been read, so none in a pane is said to have gone.

## Do and don't

- Don't type into more than one terminal at once. Studio never types into a session for you.
- Below 900px the deck shows one pane, the highlighted one, and hides the layout picker, the keys and the dividers; the tabs still switch, and the wider arrangement comes back as the window widens.

## Accessibility

- The tabs are a tablist controlling the panes; the layout picker is a radio group named "Panes"; the dividers are separators named "Width of the panes" and "Height of the panes".
- The note beside Find and Find's own result are status regions.
- Panes rise in under full motion only.
