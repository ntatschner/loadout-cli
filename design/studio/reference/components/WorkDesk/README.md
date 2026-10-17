# WorkDesk

WorkDesk is **Pure Focus**, the fifth mode: a flat, sharp work view for acting fast, with the mouse or the keyboard, and every key printed beside what it does.

## The panes

| Pane | What is in it |
|---|---|
| **List** | Everything live in one dense, grouped list: Needs you first, then Sessions, Team runs, Ideas and Tasks. A filter (`/`) on top. The picked row has a solid accent bar. |
| **View** | The picked thing: a session's terminal, a team run's question, shape and done-when (the Teams parts), an idea or a task. |
| **Actions** | Its actions, numbered 1 to 9, each with the command it runs (ActionPane). |
| **Command bar** | Along the bottom: actions, places and any loadout command (CommandBar). |

## Keys

| Key | Does |
|---|---|
| J, K (or arrows on the list) | next, previous |
| Home, End | first, last |
| Enter | open the picked one in the view (a terminal takes focus) |
| Esc | back to the list; cancels an armed action |
| 1 to 9 | run that action |
| / | filter the list |
| : | the command bar |
| G then H, S, T, K, N | go: home (everything), sessions, team runs, tasks, what needs you |
| G then I, P, C, M, U, O | go to a page, as its own desk: Ideas, Projects, Context, Memory, Spend (usage), Settings (options) |
| F6, Shift+F6 | next, previous pane |
| ? | every key (KeyHelp) |

- Single keys work whenever you are not typing. Inside a terminal every key goes to the agent, and Ctrl+] leaves it, as everywhere in Studio.
- The keys respect Studio's "Single-letter shortcuts" setting. When it is off, use the buttons, the command bar and F6.

## The look

- `[data-mode="pure"]` sets `--glow-on: 0` and `--recede-now: 1`, and stops every loop: no breathing, pulsing, flapping or travelling light. A state is still its word, its glyph and its colour; the glyph is steady.
- Hard 1px edges, solid fills, dense rows (44px), and your own theme, accent and density.
- The only motion is `ls-ack`, a 120ms accent flash on what you just ran. None under no motion.

## Narrow

- Below 1024px the actions move under the list and the view. Below 700px one pane shows at a time: the list, then the view with a List button. The actions become a row of large buttons along the bottom, and the command bar stays.

## Accessibility

- `role="application"` with an `aria-roledescription`, so screen readers pass keys through; the list is a listbox with `aria-activedescendant`; the panes are labelled regions; the command bar is a combobox.
- Every key also has a button, and every button says its key.
