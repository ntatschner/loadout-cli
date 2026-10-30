# RunBoard

RunBoard is Team Focus: a departures board of every team run. Each run is one row: what it is for, a small line of its rounds with a dot for each node briefed, its state, and its spend. The run that needs you is first, with its question answerable in the row. Teams run along the top.

## Behaviour

- Rows come in like a departures board, flapping down one after another; a running round has a light travelling into it, and its station breathes. Under reduced or no motion they are simply there.
- Order: live work first (`liveFirst` on each row's `word`): what waits on you, then running, then what you might pick up (failed, blocked, stalled), then the ended; the oldest wait first in each (`since` or `at`).
- Picking a run opens its page **beside** the board (the run page's own parts: Gate, RunShape, DoneWhen, Crew, the five depths), and the board stays where it was. Picking a team opens that team's page the same way. Escape or Close gives the board its width back.
- Projected spend past a budget says "projected over" in words.
- Each row's rounds are drawn by main's MiniLine (stage 14's `MiniLine`), the same line main's board row uses.
- Its round is said only when it has one, as main.

## Do and don't

- The mini line is decoration (`aria-hidden`). The row's words carry the run: goal, team, project, round and state.
- Below 900px the line moves under the goal, and a picked run opens as the whole screen. The row folds the same way whenever the board itself is narrower than about 780px (Team Focus with a run open beside it), so a question under a row wraps inside the column rather than running past its edge.
