# TopicEditor

TopicEditor is the centre of the Memory page: one topic, read and edited in place.

- The head: the topic's name in mono, what it is about, its store, kind, when it was written and the version it was read at.
- The text in a large mono box, with its lines and characters and whether it is "edited, not saved".
- **Save** shows the terminal equivalent, `loadout memory replace <topic> --project <slug> --file <path> --expect <version>`, and stays disabled with "nothing changed yet" until something has. **Discard changes** appears once something has.
- A topic that cannot be edited here (This machine only, or Studio only watching) is shown read-only, with why and the `memory replace` line to use in a terminal.

## When somebody else changed it

A save carries the version it was read at. If the topic changed meanwhile, nothing is saved and the editor becomes two columns:

- A Needs banner: "Somebody else changed this topic while you were editing".
- **On disk now**, read-only, with the lines your edit does not have marked − on a Needs wash (and "only on disk:" for screen readers).
- **Your edit**, still editable, so you can merge what you want into it.
- **Keep my edit, then save** saves yours against their version; **Take theirs** drops your edit and changes nothing.
