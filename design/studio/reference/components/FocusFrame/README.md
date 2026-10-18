# FocusFrame

FocusFrame shows one thing as the whole screen: a session's terminal, or a team run's question, shape and done-when. Under full motion it grows out of the line you pressed (a clip from that line's box to the full screen); under reduced motion it fades; under none it is just there.

- A slim head: the title, its project and agent (or team), its Signal, and **Back** (Escape).
- Back returns focus to the line you opened it from.
- It is a region over the page, not a modal dialog. Set `--ls-focus-top` to the ModeBar's height so the bar stays above it: its waiting count still works, and opening a question from it replaces what is shown.
