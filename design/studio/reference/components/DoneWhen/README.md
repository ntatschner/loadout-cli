# DoneWhen

DoneWhen is what a run is judged on, and what became of each criterion: its verdict, what the lead took it to mean, the evidence it cited, and what was delivered, checked against what the workers themselves reported.

## Verdicts

| Verdict | Word | Family |
|---|---|---|
| met | met | Good, a check |
| unmet | unmet | Bad, a cross: it was tried and needs a different approach |
| not-attempted | not attempted | Rest, dashed: a whole area of the goal was missed |
| none yet | no verdict | Rest, a question mark: read, but the run stopped before a verdict |

## What you provide

- `criteria`: the text exactly as written, `project: true` for the project's own (shown with a "project" chip), the verdict, `meaning` (the lead's last stated reading), `evidence`, `delivered` (refs a worker's own report handed back), and `cited` (refs the lead cited that no worker reported).
- `by`: whose criteria they were: "set by you", "the team's own", "proposed by the lead, agreed by you", or "proposed by the lead; nobody was asked" for an autonomous run.
- `goal`: how the lead read the goal itself.

## Do and don't

- Do show the reading beside every verdict. "The tests pass" read as "the new test passes" can be met while the suite is red.
- Do show a criterion with no verdict rather than leaving it out.
- Do mark "cited, but no worker reported it" in Needs text with a "!"; the lead cannot count as delivering what it judges.
- Don't call a run with no criteria done in Good's voice: say "No criteria. Done is the lead's word for it."

## Accessibility

- A list; each item reads verdict, criterion, reading, evidence, in that order.
