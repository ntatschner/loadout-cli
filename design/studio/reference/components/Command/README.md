# Command

Command shows a `loadout` command as text, either inline under an action (`.ls-cmd`) or as a block (`.ls-cmdline`) for things only a terminal can do.

## When to use it

- Inline, under every action that changes something: HaloButton does this for you with `cmd`.
- As a block, in place of a button, for the two things Studio never does itself: reading a secret back, and agreements that must be made in a terminal.

## What you provide

- `command`: the exact command, starting with `$ `. Use placeholders in angle brackets (`<draft>`), never invented values.
- `why` (block only): one sentence saying why this is done in a terminal.

## Do and don't

- Do keep commands exact and current with the Loadout version Studio is served by.
- Don't shorten a command to fit. It wraps anywhere instead.
- Don't style a command as a link or a button.

## Accessibility

- Commands are real text in `<code>`, so they can be selected and copied, and are read in order after the control they belong to.
- `subtle` and `ink-strong` keep commands at 4.5:1 or better (7:1 in high contrast).
