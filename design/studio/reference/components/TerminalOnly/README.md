# TerminalOnly

TerminalOnly is the block for what Studio never does from a page: reading or setting a secret, agreements, and the passphrase that guards the page itself. It shows the command, why it is done in a terminal, and a **Copy** that copies the line and runs nothing.

- A neutral left edge and a "terminal only" tag with a lock: it is not a warning and not a disabled button.
- `why` completes "It …" with the reason from Studio's coverage list, word for word.
- `fact` carries a state that can be shown safely: "Passphrase: set" or "not set", never the value.
- Inside an ActionDrawer with `effect: "terminal"`, the drawer's body is this block.
- **Copy** says "✓ Copied. Paste it in a terminal." once the clipboard has it. Where the browser refuses (no clipboard permission, or a page that is not secure), it says "Select the line to copy it." in the same status line instead, and nothing is thrown.
