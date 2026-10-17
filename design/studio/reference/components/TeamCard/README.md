# TeamCard

TeamCard is one team in "Teams you can run": its name, where it comes from, what a run of it does, its shape (nodes, lead, posture, budget, done-when), anything that stops it running here, and Run it.

## What you provide

- From `loadout team list` and `team show`: name, description, nodes, lead, posture, budget, how many done-when it carries, and `origin` ("ships with Loadout", "yours", "this project's").
- `problems`: anything that stops it running on this machine, in words. The card shows them in Bad with the blocked Signal, and Run it says why it is unavailable.
- `template: true` for a shape to copy (`product-company`): a dashed edge, "a shape to copy", and Make a team from it instead of Run it.

## Do and don't

- Do open Start a run with the team picked; Run it starts nothing by itself.
- Don't offer a template as something to run.

## Accessibility

- An `article` named by the team; the name links to the team's page.
