# Toasts

Toasts tell you what just happened, top right and away from the rail, at one of four levels; each says the command that ran, and anything that needs you can be answered inside it.

## Levels

| Level | Looks | Folds | Goes |
|---|---|---|---|
| Done | thin `good` edge | after `dur-toast-fold` (6s) | after a minute |
| For your information | thin `moving` edge | after 6s | stays as a chip, the newest three |
| Needs you | amber edge that breathes, `needs-wash`, answers inside | once answered | a minute after |
| Problem | red-and-ground striped edge, `bad-wash`, read out at once | once acknowledged | a minute after |

## What you provide

- An empty element for the stack; `Toasts(el)` makes it the notification region and adds the two live regions beside it.
- `add({ level, text, cmd, actions })`. `actions` are HaloButtons (`label`, `cmd`, `kind`, `onPress` returning the words to show once done).

## Do and don't

- Do give every Done toast the command that ran.
- Do put the answers to a question in the toast, with their commands; an answer settles the toast to Done.
- Don't toast what the screen already shows changing in place, unless the person is likely looking elsewhere.

## Accessibility

- Done, For your information and Needs you are announced politely; Problem is announced at once (`role="alert"`).
- Nothing folds while the pointer or keyboard focus is on the stack, so nothing moves under the person.
- A folded chip is a button that reopens the toast. The close button is 32px.
