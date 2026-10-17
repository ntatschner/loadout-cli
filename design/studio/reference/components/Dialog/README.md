# Dialog

Dialog is Studio's modal: a projected panel that opens as a line of light and unfolds to full height over a dim, while the page behind steps back.

## When to use it

- For a task that needs a few inputs before it runs (New session) or a choice that cannot be undone (Sign out every browser). Everything else is answered in place.
- `danger: true` with `role: "alertdialog"` for anything destructive; its action is a `danger` HaloButton with its command.

## What you provide

- `title` and `why`: one sentence on what happens and what it costs.
- `content`: the inputs (a StartRail, Pickers, a ModePicker).
- `actions`: HaloButtons with their commands. A Cancel is added after them. An action closes the dialog unless its `onPress` returns `true`.
- `behind`: the page element, which steps back (scaled, blurred and dimmed in Dark; only dimmed in the other themes).

## Do and don't

- Do say what the main action runs, under it.
- Don't stack dialogs. Don't put a dialog in front of a question that could be answered in place.

## Accessibility

- `role="dialog"` (or `alertdialog`) with `aria-modal` and its title as its name; focus moves in, is held there, and returns to the opener on close.
- Escape and the dim close it. A `danger` dialog opens with focus on Cancel, so Enter never does the destructive thing by accident.
- Under reduced or no motion the panel fades in instead of projecting.
- In high contrast the panel has a 2px edge in its colour and no glow.
