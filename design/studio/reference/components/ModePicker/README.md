# ModePicker

ModePicker chooses one of a few options that each need a sentence of explanation, such as a session's mode; the command beneath changes as you choose.

## What you provide

- `options`: `{ name, desc }`. Names are the CLI's words (`advise`, `investigate`, `implement`, `review`), shown in capitals; `desc` says what it means in one sentence.
- `command(name)`: the command the choice leads to. `onChange(name, index)`.
- `label` for the group.

## Do and don't

- Do use it for two to six options. More than that wants a picker in a popover.
- Don't use it for on/off: that is a Switch.

## Accessibility

- `radiogroup` and `radio` roles with `aria-checked`; arrow keys, Home and End move and pick; one tab stop for the group.
- The chosen ring is filled, not just recoloured.
