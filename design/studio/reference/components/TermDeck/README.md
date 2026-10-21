# TermDeck

TermDeck is Terminal Focus: the session tabs, a pane layout, and the terminals themselves as big as the screen allows. The side rail stays folded to icons and the page gives the deck its full height.

## What it does

- **Tabs** (SessionTabs) over **one, two side by side, or four** panes. The session you pick is always shown; the others fill the remaining panes in tab order. Panes keep their places: focusing one only moves the accent ring, and a session picked from its tab takes the highlighted pane without moving any other. A session that ends stays in its pane until something else is picked there; one gone from the list altogether gets a note saying so. A tab hidden or cleared off the strip gives its pane to the next tab along.
- **Keys**: Ctrl+Alt+1 to 9 picks a session, Ctrl+Alt+arrows move between panes, Ctrl+] leaves a terminal (as Terminal already does). Typing goes to the focused terminal only.
- **Go to the one waiting** appears when a session needs you.
- **Find in this terminal** marks every match in the focused terminal's output and scrolls to the first. With xterm.js this is its search add-on (`@xterm/addon-search`, MIT), themed from `needs`.
- Each pane is a full Terminal, with its own size, A−, A+ and Fit.

## What you provide

- `sessions` (id, title, state word, project, agent, `unread`), `terminal(id)`, which returns a Terminal for that session (the deck never makes connections itself), and `layout`.
- Remember the layout, which session is in each pane (`slots`, handed back through `onslots`) and the last session in this browser.

## Do and don't

- Don't type into more than one terminal at once. Studio never types into a session for you.
- Below 900px the deck shows one pane and hides the layout picker; the tabs still switch.
