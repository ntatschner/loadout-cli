# RoleRecord

RoleRecord is what this machine has learned about its team files: one row per role and model, dearest first, with how many runs it has been in, what it has cost in all and on average, how often its reports were taken, and how many tool calls it is refused per run. Under it, where each node's time went, roughly.

These are questions about the team file, not about one run: every run of one role refusing twenty tool calls is a role whose permissions are written wrong.

## Behaviour

- Folded by default, and read when opened, not on every refresh: it walks every journal on the machine (`/api/roles`). While reading it says so.
- Split by model as well as role. A row whose model was never recorded says "not recorded".
- A refused count at or above `refusedHigh` (10) says "high" in words, not only in colour.
- Time: deciding, tools running, writing, and waiting (any gap over two minutes). Its heading says "roughly", because deciding includes the time an answer took to arrive.

## Accessibility

- A `details` with a real `summary`. The table keeps its headers and becomes labelled stacks when narrow. The time bars are decoration; the sentence above each says the numbers.
