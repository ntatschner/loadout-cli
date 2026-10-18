# QuestionSet

QuestionSet is how an idea's questions are answered on the Ideas page. Each question is a card with a Needs edge: the question, why the agent asks, its options as large radio chips (the recommended one says so), and a box for your own words. A chosen or typed answer turns the edge to the accent: yours, not sent yet. An answered question turns Good and shows "You answered: …" with **Change it**.

Nothing is sent until you press one of the two buttons at the foot:

| Button | Runs |
|---|---|
| **Send answers and refine** (solid) | `loadout idea answer <id> <question> --answer=<text> --project <slug>` for each changed answer, then `loadout idea refine <id> --project <slug>` |
| **Only save the answers** | the answers alone |

- The answer is joined with `=` because the parser refuses a value that starts with a dash given any other way.
- "The commands, one per answer" lists every exact line before you send.
- A refine is a round on the daemon: the page can be left, and a toast says when it ends.
