# Spend

Spend is the spend line for a run: what it has spent, of what, and where it is projected to end. Past its budget is said in words with a Needs edge, never in colour alone. Main draws it on every RunCard and in the Spend zone of a run's page. Ported from main's `studio/src/lib/components/Spend.svelte` as built.

## What it shows

- **The words**, in mono: what it has spent, "of $5.00" (or "no cap, on purpose"), and the rate ("· $0.41 a round") when there is one.
- **Projected past its budget**: "projected $6.85, past its budget" in Needs, and a Needs edge round the bar.
- **The bar**, when there is a cap: the share spent in Moving, and a Needs tick where it is projected to end, while that is still inside the cap.

## What you provide

- `spent`, and `cap` (null for none), or `uncapped: true` for no cap on purpose.
- `projected`, when the daemon has worked one out, and `rate` as words.
- `set(patch)` takes new figures as the run goes.

## Do and don't

- Do give `uncapped` for a run started with no cap: it says so, rather than look like a missing figure.
- Do put a "Change what it may spend" button beside it on a running run (`$ loadout team budget <run> --usd <figure or none>`); the line itself changes nothing.
- Don't add colour for "over": the words say it, and the edge marks it.

## Accessibility

- The bar is a `role="meter"` named "Spent", with its figures and, when over, "projected …, past its budget" in `aria-valuetext`.
- Over budget is a word as well as the Needs colour.
