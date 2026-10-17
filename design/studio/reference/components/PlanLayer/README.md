# PlanLayer

PlanLayer is one layer of an idea's plan: its id and name, its verdict (undecided, keep, drop, improve), what it is for, and its options **side by side** as cards, so they can be compared. Each card has the option's detail, what is for it (+) and against it (−), "recommended" when the agent recommends it, and "chosen" with a filled edge when it is chosen.

| Action | Runs |
|---|---|
| **Choose this** (on a card) | `loadout idea choose <id> <layer> <option> --project <slug>` |
| **Keep it** | `loadout idea keep <id> <layer> --project <slug>` |
| **Drop it** | `loadout idea drop <id> <layer> --project <slug>` |
| **Ask for it to be reworked** | opens a sentence box, then **Send it back**: `loadout idea improve <id> <layer> --request=<sentence> --project <slug>` |

- An addition (`addition`) is drawn the same way without options: why it is proposed, Keep it, Drop it, Ask for it to be reworked.
- A dropped layer fades, but stays readable.
- Once the idea is accepted (`accepted: true`) the layer shows what was chosen and offers nothing.
- The For and Against marks carry their words for screen readers.
