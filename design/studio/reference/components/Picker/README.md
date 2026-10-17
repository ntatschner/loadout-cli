# Picker

Picker chooses one value from a list in a popover that hangs from the control on a light tether: a line grows from the control, then the list unfolds from its end.

## What you provide

- `label`, written in sentence case.
- `options`: `{ name, sub }`. `name` is the value as the CLI writes it (`starstats`, `database.postgresql`); `sub` is one short fact in mono (the path and what is running, what a specialist needs).
- `selected` and `onPick(option, index)`. Put the command the choice changes beside or under the form.

## Do and don't

- Do use a Picker when there are more than six options, or when each needs a fact beside it. Fewer, with a sentence each, is a ModePicker.
- Don't put actions in a Picker. A list of things to do is the CommandPalette.

## Accessibility

- The trigger has `aria-haspopup="listbox"` and `aria-expanded`; the list is a `listbox` with `aria-activedescendant`.
- Arrow keys, Home and End move; Enter or Space chooses; Escape closes and returns focus to the trigger; a click outside closes it.
- The active option has a 2px `focus` outline as well as the accent edge.
