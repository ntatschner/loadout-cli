# ItemList

The things a command keeps, as a list, each with what can be done to it, and a way to add one. It replaces the separate drawers a page used to have for listing, adding, changing and removing the same kind of thing (packs, MCP servers, secrets, checkpoints, backups, shared tools, voice guides, specialists).

## What it shows

- A **title**, how many there are, and **+ Add one**, which opens the add form (an ActionForm) in place.
- One **row** per thing:
  - its name and where it is from;
  - its state, as a Signal;
  - its facts, in mono;
  - its **buttons**: Update, Approve, Restore, Remove and so on. **Details** opens what a reading command says about that one thing, under the row.
- **The command under the row:** for whichever button is pointed at or focused, the exact line it runs. With nothing pointed at, it shows the first button's.
- Anything that can't be undone asks first, by name, in a Dialog that shows the line.
- **Where the list is from:** the reading command with `--json`, and when it was read.

## Do and don't

- Do read the list with the command's `--json`, and draw it, never print its text.
- Do let a removed row leave the list at once, and say so under it.
- Don't put more than four buttons on a row: the rest go in Details.

## Accessibility

- It's a region named for its title, and the rows are a list.
- The buttons are real buttons in reading order, and the line under the row follows focus.
- What a button did is said in a status line.
