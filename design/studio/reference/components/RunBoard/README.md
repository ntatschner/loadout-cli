# RunBoard

RunBoard is Team Focus: a departures board of every team run. Each run is one row: what it is for, a small line of its rounds with a dot for each node briefed, its state, and its spend. The run that needs you is first, with its question answerable in the row. Teams run along the top.

## Behaviour

- Rows come in like a departures board, flapping down one after another; a running round has a light travelling into it, and its station breathes. Under reduced or no motion they are simply there.
- Order: needs you, then bad, then moving, then the rest.
- Picking a run opens its page **beside** the board (the run page's own parts: Gate, RunShape, DoneWhen, Crew, the five depths), and the board stays where it was. Picking a team opens that team's page the same way. Escape or Close gives the board its width back.
- Projected spend past a budget says "projected over" in words.

## Do and don't

- The mini line is decoration (`aria-hidden`). The row's words carry the run: goal, team, project, round and state.
- Below 900px the line moves under the goal, and a picked run opens as the whole screen.
