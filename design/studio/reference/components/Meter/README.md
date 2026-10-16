# Meter

Meter shows an amount against a threshold you set: a beam that fills, a mark where the threshold is, and, past it, the fill turning to Needs with the words saying so. ProgressRing is its round form for stages.

## What you provide

- `value`, `max`, and `threshold` (optional). `format` and `unit` for the figure, `of(value)` for the context beside it ("of a 60,000 daily threshold").
- `thresholdLabel` under the mark ("daily threshold", "warns at 80%") and `source`: where the figure comes from, or the command that changes the threshold.
- `overWords` (default "past your threshold").

## Do and don't

- Do say amounts in tokens. Loadout's spend thresholds (DailyTokens, ProjectDailyTokens, PlanWarnAt) say where you stand and stop nothing, so never word a threshold as a limit or a block.
- Do write the figure beside the beam every time.
- Don't use a meter for stages: that is a ProgressRing.

## Accessibility

- `role="meter"` with `aria-valuenow`, `aria-valuemax` and an `aria-valuetext` that includes "past your threshold".
- Crossing the threshold changes the colour, the words and the value text together.
- In high contrast the track gets a `control-edge` border.
