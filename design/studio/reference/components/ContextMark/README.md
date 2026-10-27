# ContextMark

ContextMark is how much of a session's context window is in use, small enough for a tab or a list row: the figure and a short beam, turning to Needs as it fills and to Bad when it is nearly full. Nothing here is announced as it changes; the strip on the terminal says when a threshold is crossed. Ported from main's `studio/src/lib/components/ContextMark.svelte` as built.

## What it shows

- A 28px beam filled to the share and the share as a figure ("42%").
- Past 70%: Needs. Past 90%: Bad, bolder, with a **!**.
- Nothing at all while Claude has not said (`percent: null`).

## What you provide

- `percent`: the share in use, rounded (main's `contextPercent(status)`), or null. `set(percent)` changes it.
- On a session tab, put it after the where and agent line and before the key: the tab's grid moves it to the top right and the key to the second row (`.ls-stabs [role="tab"] .ls-ctx`). The same on a WorkDesk row (`.ls-desk [role="option"]`).

## Do and don't

- Do use it where a session is one row among many. On the terminal use StatusStrip; in Details, StatusBlock.
- Don't announce it. It changes with every redraw.

## Accessibility

- `role="img"` with the words in its label and title: "Context 93% used, nearly full". The beam and figure are `aria-hidden`, so it is read once.
- The level is said in words as well as colour: "filling", "nearly full", and the "!" on Bad.
