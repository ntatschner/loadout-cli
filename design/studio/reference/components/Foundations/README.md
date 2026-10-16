# Foundations

Foundations shows the four themes side by side, with the accent and density pickers that every Studio screen honours.

## What you set

- `data-theme` on `<html>`: `dark` (the default), `light`, `hc-dark` or `hc-light`. When the person has not picked one, start from `LoadoutStudio.preferredTheme()`, which follows the operating system's colour scheme and contrast preferences.
- `data-accent`: `cyan` (the default), `ice`, `magenta` or `violet`. It rebinds `accent`, `accent-text` and `on-accent`, and every glow follows.
- `data-density`: `compact`, `comfortable` (the default) or `roomy`. It sets `--density`, which multiplies the spacing scale.
- `data-motion`: `full`, `reduced` or `none`. Read the level in force with `LoadoutStudio.motion()`, because the operating system's reduced-motion preference always wins.
- Put `class="ls"` on `<body>` for the ground, ink, typeface and focus ring.

All four can also go on one panel instead of `<html>`, as this page does, to show a preview in another theme.

## Do and don't

- Do save the four choices in Loadout's config, so every browser signed in to this machine opens Studio the same way.
- Don't use `accent` to say anything about state. State is the five signal families, with a word and a glyph.
- Don't reach for `accent-cyan` and the other named accents directly. Read `accent`, so the person's pick applies.
