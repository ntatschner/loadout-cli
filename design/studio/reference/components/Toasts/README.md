# Toasts

Toasts tell you what just happened, top right and away from the rail, at one of four levels; each says the command that ran, and anything that needs you can be answered inside it. As main's `Toasts.svelte` and `toasts.svelte.ts`.

## Levels

| Level | Looks | Folds | Goes |
|---|---|---|---|
| Done | thin `good` edge | after 4 seconds | after 10, into Earlier |
| For your information | thin `moving` edge | after 4 seconds | after 10, into Earlier |
| Needs you | amber edge that breathes, `needs-wash`, answers inside | 4 seconds after it is answered | 10 seconds after |
| Problem | red-and-ground striped edge, `bad-wash`, read out at once | 4 seconds after it is acknowledged | 10 seconds after |

Each toast counts its own seconds: the one under the pointer or holding the keyboard stops while the rest go on. A folded chip is a button that opens it again for another run of the times. The same Done or notice arriving again while it shows counts up (`×3`) instead of stacking.

**Earlier** collects everything that left the stack this session (the newest 50), with its command; it opens as a list with a Clear.

## Places

`place(p)` (or `Toasts(el, { place })`) says where the person is: `"session"` (a session's page), `"pure"` (Pure Focus) or `"desk"` (a page desk in Pure Focus); `null` anywhere else. In a named place everything arrives as a chip, and Done and notice chips go see-through and let clicks pass while the pointer is within 48px of them, so they are never in the way. A question waiting on you never gives way. Leaving the place opens again whatever still needs someone.

## The key

**Ctrl+Shift+X**, caught before a terminal takes it, folds whatever is open; pressed again with nothing open, it sends everything not waiting on you into Earlier.

## What you provide

- An empty element for the stack; `Toasts(el, { place, clock })` makes it the notification region and adds the two live regions beside it. `clock: false` leaves the seconds to the page, which calls `tick()`.
- `add({ level, text, cmd, actions })`. `actions` are HaloButtons (`label`, `cmd`, `kind`, `onPress` returning, or resolving to, the words to show once done; nothing uses the label).
- `settle(id, words)`, `open(id)`, `dismiss(id)`, `sweep()`, `place(p)`, `clearHistory()`, `stop()`.

## Do and don't

- Do give every Done toast the command that ran.
- Do put the answers to a question in the toast, with their commands; an answer settles the toast to Done.
- Do tell the stack the place whenever the route or mode changes.
- Don't toast what the screen already shows changing in place, unless the person is likely looking elsewhere.

## Accessibility

- Done, For your information and Needs you are announced politely (a count says "3 times"); Problem is announced at once (`role="alert"`).
- The toast under the pointer or keyboard does not fold or leave, so nothing moves under the person.
- Opening a chip puts the keyboard on the toast's dismiss button; answering a question leaves it there. The close button is 32px.
- The see-through chips follow `motion()`: no fade below full motion.
