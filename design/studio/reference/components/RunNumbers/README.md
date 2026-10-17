# RunNumbers

RunNumbers is the top of What it cost: the numbers that change what somebody does, rather than decorating the page.

## The three panels

- **What it is costing.** The rate, then where it ends up if the run uses the rounds it has left: "Projected $26.30 by round 4, past its $20.00 budget." Projected from rounds, not the clock, because a run spends per round. Past the budget, the panel gains a Needs edge and the Signal "projected over", in words as well. The rate is said per hour once it drops below a penny a minute.
- **Where the time went**, by round and then by node, as bars in the page itself (no canvas, no chart library). Each row says the name and the number.
- **What went wrong**, counted by kind: tool calls refused, reports the checker would not accept, turns asked again, rounds that asked for nothing, branches that would not merge. Zero reads "none", dimmed.

## Do and don't

- Do take every number from what the run wrote down while it happened. A run recorded before a number existed does not show it.
- Don't colour a bar to mean good or bad; bars are amounts, in the accent.

## Accessibility

- Each panel is a labelled section. Bars are decoration; the row's text carries the number.
