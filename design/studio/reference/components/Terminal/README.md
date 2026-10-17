# Terminal

Terminal is the agent's own screen inside Studio: a slim bar saying what the session is doing, the screen itself in Studio's terminal palette, and, when the session needs something or has stopped, a bar underneath that says so in words.

## Build it on xterm.js

- This component is the reference look. Build the real one on xterm.js (MIT), with its theme taken from the tokens: `background` term-bg, `foreground` term-fg, `cursor` term-cursor, `cursorAccent` term-bg, `selectionBackground` term-selection, and the sixteen ANSI colours from `term-black` to `term-bright-white`.
- Set `minimumContrastRatio` to 4.5, or 7 in the high-contrast themes, so an agent's own 24-bit colours are lifted until they read. The sixteen ANSI colours already meet it.
- Set the font to `var(--font-mono)` (JetBrains Mono), 13px by default, line height 1.4.
- Turn on xterm's screen-reader mode when the operating system reports a screen reader, and give the terminal the accessible name "Terminal: <session title>".

## The bar

- The session's Signal, then project and agent, then the size (80 × 24).
- Text size A− and A+, and Fit, which scales 80 columns to the width. On a phone, Fit is on: the terminal keeps 80 columns and scales down; zooming in scrolls inside the terminal, never the page.
- While the terminal has focus, the bar says "Ctrl+] leaves the terminal".

## Typing

- Keys go straight to the agent, Tab and arrows included, as in a real terminal. Ctrl+] hands focus back to the page (the next control, or `leaveTo`), so a keyboard user is never stuck.
- Studio never types into a session for you. A question in a session is answered in its terminal.

## States

| State | Word | What the bar under the screen says | Typing |
|---|---|---|---|
| connecting | connecting | nothing | off |
| working | working | nothing | on |
| idle | idle | nothing | on |
| unknown | unknown | nothing | on |
| waiting | waiting for you | "It has finished and is waiting for your next message." with Type a reply | on |
| approval | needs approval | "It is asking for approval. The choices are in the terminal; answer them there." with Go to the question | on |
| ended | ended | "Ended. Its transcript is kept." and `$ loadout resume <session-id>` | off |
| error | exited with an error | the exit code, and the resume command | off |
| reconnecting | reconnecting | "Lost touch with the daemon. Reconnecting; the output so far is kept." with Try now | off |
| watch | only watching | "Only watching: typing is off. Start Studio without --watch-only to type into a session." | off |
| lost | lost | "This session ended when the daemon restarted. Its transcript is kept." and the resume command | off |

- **Where waiting and approval come from.** Only from the agent's own hooks, which Loadout installs at launch. Claude Code reports both (its `idle_prompt` and `permission_prompt` notifications). Codex shows only working, idle or unknown until its notify events are verified. Studio never guesses a question from the text on screen.
- Working, idle and unknown come from what Loadout already knows: whether the session has written anything recently (`studio-idle-after`, 10 minutes by default).

## Do and don't

- Do keep the terminal's own colours inside the screen. The bar and the state bar use Studio's tokens.
- Don't put the agent's choices on buttons. Studio is told that a question is waiting, not what the choices are.
- Don't log what the terminal shows.

## Accessibility

- The screen is focusable and named; its output is a log. The state bar is a live region, so a new question is announced once.
- Every terminal colour is 4.5:1 or better on term-bg and term-selection (7:1 in high contrast); the floor lifts anything else.
- The cursor blinks only under full motion.
