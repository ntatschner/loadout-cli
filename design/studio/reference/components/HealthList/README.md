# HealthList

HealthList shows findings from `drift`, `repo check`, `doctor`, `memory audit`, `rules audit` and the instructions audit in one shape: worst first, each with its severity as a **word and a glyph** (✕ error, ! warning, i info, ✓ fine) and an edge in its colour, so colour never carries it alone.

- A summary line says how many errors and warnings there are and how many can be put right. With a `verdict` it starts "The audit says …".
- A fixable finding carries "can be put right". The page puts its fix next to the list, as an ActionDrawer (`drift --fix --yes`, `doctor --fix --yes`, `memory audit --clean`).
- `from` names the command the list comes from.
- An empty list says so in Good: "✓ Nothing to put right."
