# ProgressLine

ProgressLine answers "is this run getting closer?" in one line, for run cards, the Needs you list and the Team Focus board:

- **"3 of 5 met"**, then ", up from 1" in Good when more are met than in round 1;
- **"last moved 2 rounds ago"**: the last round any done-when changed its verdict;
- **"stalled ·"** in Needs when the run has been held for not moving;
- a small line of the met count by round, with a dashed line at "all met". The line is decoration (`aria-hidden`); the words carry it.

A run with no done-when says "No done-when to judge it by." The figures come from the verdicts by round (see CriteriaGrid); the run's page shows them in full.
