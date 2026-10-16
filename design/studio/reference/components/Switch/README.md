# Switch

Switch turns one setting on or off, with On or Off written beside the light and, underneath, what it does and where the choice is kept.

## What you provide

- `label` and `sub`. Say where the choice is kept ("kept in this browser", "kept in Loadout's config") and any cost ("covers your editor, so it starts off").
- `checked`, `onChange(on)`.
- When a switch changes Loadout's config rather than this browser, show the command under it with a `.ls-cmd`.

## Accessibility

- `role="switch"` with `aria-checked`; the word On or Off repeats the state, so it never rests on colour or position.
- The track's edge is `control-edge` (3:1 or better) when off and `accent` when on.
- The whole row is the target, at least `control` (44px) tall.
