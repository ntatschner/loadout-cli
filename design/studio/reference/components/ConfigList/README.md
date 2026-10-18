# ConfigList

ConfigList is "Loadout's settings" on the Settings page: every setting `loadout config` knows, in the groups the terminal's Settings screen uses, each changed where it is shown.

- A search box finds a setting by name or by what it does, and opens every group with a match; the count says "4 of 48 settings".
- Each row: the name in mono, "this machine only" when it never syncs, "set" when it has a value, what it does, and "Unset, it uses …" when it has no value.
- A flag is a Switch; anything else is a text box whose placeholder is its default.
- Changing a value marks the row with the accent edge and offers **Set it**, with the exact `loadout config set <name> <value>`, and **Put it back**. A set value can be **Cleared** (`config set <name> ""`), which puts it back to its default.
- **Explain it** runs `loadout config get <name> --explain`, which reads only, and shows the answer under the row.
- Group headings show how many settings each has and how many are set.
