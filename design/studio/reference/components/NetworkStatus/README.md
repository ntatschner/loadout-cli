# NetworkStatus

NetworkStatus is the band above the map that says, at a glance, how many pieces of work are at each station, how long the oldest question has waited, the tokens used today, and which lines are disrupted, then "Good service on every other line".

## What you provide

- `lines`: the map's lines; counts and disruptions come from them (blocked and failed lines are disruptions).
- `clock`, `oldest` ("4 min · loadout-cli" or "none") and `tokens` (today's figure and the daily threshold, in tokens).

## Do and don't

- Do keep it to one band. Detail belongs in the LineStrip.
- Don't call a threshold a limit: it says where you stand and stops nothing.

## Accessibility

- A labelled section; the counts are a list, and each count is written with its station name and glyph, never by colour alone.
- Below 600px the band stacks into one column.
