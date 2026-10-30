# ActionRow

Things a page does with one press and no input: rebuild the memory index, bring the workspace up to date, open a project in its editor, send a test call for help, let held schedules fire.

## What it shows

- An optional title and why.
- Each action as a HaloButton named for what it does, with its exact line under it.
- What the last one did, in a status line.
- Anything that can't be undone asks first, by name. An action's `onPress` may return a promise: its button (or its asking Dialog) is busy until it settles, and `false`, a throw or a rejection says "✕ why" under it.

## Do and don't

- Do use it only when there is truly nothing to choose. An action with even one input is an ActionForm, with that input in plain words.
