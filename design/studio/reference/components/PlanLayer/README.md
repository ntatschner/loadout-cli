# PlanLayer

PlanLayer is one layer of an idea's plan: its id and name, its verdict (undecided, keep, drop, improve), what it is for, and its options **side by side** as cards, so they can be compared. Each card has the option's detail, what is for it (+) and against it (−), "recommended" when the agent recommends it, and "chosen" with a filled edge when it is chosen.

| Action | Runs |
|---|---|
| **Choose this** (on a card) | `loadout idea choose <id> <layer> <option> --project <slug>` |
| **Keep it** | `loadout idea keep <id> <layer> --project <slug>` |
| **Drop it** | `loadout idea drop <id> <layer> --project <slug>` |
| **Ask for it to be reworked** | opens a sentence box, then **Send it back**: `loadout idea improve <id> <layer> --request=<sentence> --project <slug>` |

- A choice or a verdict shows the moment it is made ("chosen · recording"); while it is being recorded the other buttons do nothing (`inactive`) but keep their commands. If it is not recorded (the callback returns or resolves `false`), it goes back to what it was and says so in Bad: "✕ o2 was not recorded for L2", "✕ That was not recorded for L2".
- The verdict already given does nothing (`inactive`, announced as disabled) and keeps its command; the verdict tag says which it is.
- Nothing changes size as you choose: Choose this and the chosen marker share one slot, and the status line keeps its height when empty.
- `onRework(pending)` says whether a rework request is written and not sent. `onRequest(text)` hears its words as they are typed (and `""` once sent); a page that draws the layer again passes them back as `request`, and the box starts open with them, so nothing typed is lost. The options carry `data-next="choose:<layer>"`, the verdicts `data-next="verdict:<layer>"` and Send it back `data-next="rework:<layer>"`, for a page's Next button.
- An addition (`addition`) is drawn the same way without options: why it is proposed, Keep it, Drop it, Ask for it to be reworked.
- A dropped layer fades, but stays readable.
- Once the idea is accepted (`accepted: true`) the layer shows what was chosen and offers nothing.
- The For and Against marks carry their words for screen readers.
