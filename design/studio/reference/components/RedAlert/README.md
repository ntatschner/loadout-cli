# RedAlert

Red Alert is for when Studio sits on another screen, or the sound is off. It is **off by default**, and switched on in Settings → Studio → Ambient, keys and alerts (`studio-red-alert`).

When it is on and something needs you (a run's question, a session waiting or asking approval, a stalled run, a run that failed), two things show over every page, in every mode:

- **The edge**: a 3px line and a translucent gradient round the whole page, in Needs, or in Bad when one of them is a problem;
- **One line at the top**: "2 need you", the most urgent reason ("iterating-project asks: fix the two flaky tests here, or leave them? (and 1 more)"), **Go to it**, and **Quiet until something new**.

It clears itself when nothing needs you. Quiet hides it until the set of things changes; it is not a setting and lasts only in this browser tab.

## Motion and themes

| Setting | The edge |
|---|---|
| full motion | breathes over 2.4s (never more than three flashes a second) |
| reduced | steady glow |
| none, and Pure Focus | a still 4px border, no gradient |
| high contrast | a solid 6px border |

## Accessibility

- The line is `role="status"` with `aria-live="assertive"` and is announced when it appears; it never takes focus.
- Colour never carries it alone: the line says what, in words.
- The edge is `pointer-events: none`; only the line takes clicks. While a dialog is open the line steps aside and only the edge stays, so it never covers what you are answering.
