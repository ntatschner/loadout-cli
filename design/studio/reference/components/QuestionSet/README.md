# QuestionSet

QuestionSet is how an idea's questions are answered on the Ideas page. Each question is a card with a Needs edge: the question, why the agent asks, its options as large radio chips (the recommended one says so), and a box for your own words. A chosen or typed answer turns the edge to the accent: yours, not sent yet. An answered question turns Good and shows "You answered: …" with **Change it**.

Each card also says what the question relates to ("Relates to L2 Storage: whether the cache can be rebuilt"), and, when that may have changed since, "Possibly outdated: …. Answering clears it." An answered one that may be outdated offers **It still stands**, which sends the saved answer again and clears the marker. Above the first question of each round is what the agent understood the idea to be when it asked: "Understood as: …".

The recommendation is matched to an option the way main's `offeredChoices` does: exactly (ignoring case and spaces), else the longest option that starts it or that it starts, ending at a word; a tie counts as no match. A recommendation that matches nothing becomes a chip of its own. What is left of it after the option is shown as "Why it recommends …: …".

Nothing is sent until you press one of the buttons at the foot:

| Button | Runs |
|---|---|
| **Send answers and refine** (solid while there is something to send) | `loadout idea answer <id> <question> --answer=<text> --project <slug>` for each changed answer, then `loadout idea refine <id> --project <slug>` |
| **Only save the answers** | the answers alone |
| **Accept all recommendations** | nothing: it fills the recommended chip in every open question with nothing typed ("fills 2 answers; sends nothing"), and says "no recommendations left to take" when there are none |

- The answer is joined with `=` because the parser refuses a value that starts with a dash given any other way.
- "The commands, one per answer" lists every exact line before you send.
- A refine is a round on the daemon: the page can be left, and a toast says when it ends.
- An answer sent shows as answered until the idea is read again (`set({ questions })`); a refused send (`onSend` resolving false) marks nothing sent. While watch-only (`disabledReason`), Change it and It still stands are not offered.
- `onDraft(drafted, sent)` tells the page what is typed and not sent, for its Next button; each card carries `data-next="question:<id>"` and Send answers and refine `data-next="send-answers"`.

## Accessibility

- Each card is a `section` named by its question; an open one is a `fieldset` with the question as its `legend`, and the chips are real radio buttons in a group.
- The edge is never the only sign: the line over the question says "the agent asks", "your answer, not sent yet" or "answered".
- What happened is said in a `role="status"` line that keeps its height when empty, so nothing moves when it speaks, and a send keeps what you were looking at where it was.
