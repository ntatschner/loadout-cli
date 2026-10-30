# HaloButton

HaloButton is Studio's button: a word in spaced capitals beside a ring of light, with the command it runs written underneath.

## When to use it

- For every action. Studio has no other button shape.
- `kind: "needs"` (amber) for answers to a question that is waiting for you; `kind: "danger"` for acknowledging or stopping something that went wrong. Otherwise the accent.
- `solid: true` marks the strong answer in a set (Approve, the recommended option). Use one solid button per set at most.
- `size: "big"` for the answers to a gate, `"small"` in toasts, bars and dense rows.

## What you provide

- `label`, written in sentence case; the style sets the capitals.
- `cmd`: the exact command the press runs (`$ loadout team gate --answer Approve`). Every button that changes something shows one. Where nothing in Loadout changes, say what does happen instead ("opens here · changes nothing", "stops the agent in this terminal"). Studio never types into a session for you: a question in a session is answered in its terminal (see Terminal).
- `glyph` (optional): one character in the ring for answers (✓, ✕, ↻) or option numbers (1, 2).
- `disabledReason` instead of disabling silently: the ring goes grey and the reason is written where the command would be.
- `inactive` (optional) where the button does nothing and the page already says why beside it, such as the verdict already given: it keeps its command, and is announced as disabled.
- `busy` (optional) while what it started is working: the label gives way to the busy word ("ending…", "starting…"; `true` is "working…"), the button is inactive and `aria-busy`, and its ring breathes under full motion. `HaloButton.setBusy(button, word | false)` turns it on and off on a button already drawn; Dialog does this for its actions.
- `describedBy` (optional): the id of a line that says more about the button, read after its name. The line can be visually hidden (`ls-sr`): "3 left to decide on this idea".
- `key` and `next` (optional): set as `data-k`, for keeping focus on the button across a redraw, and `data-next`, where a page's Next button finds it.
- `onPress`.

## Do and don't

- Do put a set of answers in `.ls-answers`, the strong one first.
- Don't use a button for reading a secret back or for agreements made in a terminal (`tools verify --agree`): show a `Command` block instead.
- Don't add icons beside the label. The ring is the affordance.

## Accessibility

- A real `<button>`; the ring is `aria-hidden`. The command sits right under the button so it is read in order.
- The focus ring circles the halo, 2px in `focus`, 4px off.
- A busy button keeps its place and focus, so nothing jumps; its word is its name while it works.
- Height is `control` (44px), `control-sm` (36px) when small.
- Hover grows the ring and lights its centre; in Light and high contrast there is no glow, so the ring and dot carry it.
