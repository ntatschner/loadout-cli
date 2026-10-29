# CriteriaGrid

CriteriaGrid is on a run's page, above Done when: each done-when is a row, each round a column, and each cell the lead's verdict after that round.

| Glyph | Word |
|---|---|
| ✓ (Good ring) | met |
| ✕ (Bad ring) | unmet |
| – | not attempted |
| · | no verdict |

- A cell whose verdict changed from the round before is tinted, so where the run moved shows at a glance.
- Each row says "met since round 2", "last moved in round 3" or "has not moved since round 1"; in Needs text when a row that is not met has not moved for three rounds.
- The summary: "2 of 4 met, up from 0 in round 1 · none has moved for 3 rounds".
- A real table (`caption`, `scope`), each cell's word visually hidden. Narrower than about 520px of its own width (a phone), each done-when's words go above its rounds, which wrap in a row with each round's number over its mark (the current round underlined), so nothing scrolls sideways at 320px however many rounds there are.

**Data.** Today the run's JSON has only the latest coverage. The lead's coverage is journaled every round (`report.checked`, after `round.started`), so the server folds it into `coverageByRound` (see `ROUND4.md`). Both spellings of not attempted are accepted.
