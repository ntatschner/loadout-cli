# EventMark

EventMark records that something just happened to an item (a question arrived, a round failed, a session closed) as a small mark with its age; a single ping draws the eye when it arrives.

## What you provide

- `family`: the family of the new state; the mark takes its colour and glyph (? for Needs, ✕ Bad, ✓ Good, ↻ Moving).
- `text`: what happened, in a few words. `age`: "just now", "3 min ago".
- Call `ping(container, x, y, family)` once when the event arrives, at the item's glyph.

## Do and don't

- Do keep the mark until the person looks at the item (focuses it, opens it, or it comes into focus on Lens), then remove it.
- Don't stack marks: an item shows its latest event only.

## Accessibility

- Announce the event once in a polite live region (the toast stack does this); the mark itself is not live.
- The ping is skipped under reduced or no motion; the mark stays.
