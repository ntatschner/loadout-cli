# SayToNode

SayToNode is the box under a live node's own stream for saying something to it now, rather than to the lead between rounds. It goes into a process running with your file access, at a moment nobody chose, so it needs a **second credential** that Studio's sign-in does not grant.

## Its states

| State | What it shows |
|---|---|
| `unset` | No passphrase is set on this machine, so nothing can attach. Setting it is terminal only, because this page cannot set the passphrase that guards it: `$ loadout team attach set --passphrase "…"` |
| `locked` | A passphrase field and Unlock for 30 minutes. The daemon swaps it for a grant of 32 random bytes, not derived from the passphrase, that stops working after 30 minutes. Held in memory, never in storage. A wrong one says "That is not the passphrase." |
| `open` | A message box and Send: `$ loadout team say <run> --node <node> --message "…"`, and how long the grant has left |
| `ended` | The node is not running. Say something to the lead instead. |

With `lead: true` it is the other box: a message to the lead, `$ loadout team message <run> --message "…"`, read at the start of the lead's next round, once. Studio's sign-in is enough for that one.

## Do and don't

- Do say when it arrives: the next time the node says anything, once.
- Do show it only where the node's own stream is shown, and only while the node runs.
- Don't keep the grant past the page; it is for a person at a page.
- Don't offer Clear the passphrase here; `team attach clear` is terminal only too.

## Accessibility

- A labelled section. The passphrase field is `type=password` with `autocomplete=off`; a refusal is an alert, a send a status.
