# Urgency

Urgency decides which one thing in a list most needs you, raises it, and lets everything else recede, so the eye lands in the right place without reading the list.

## What you provide

- The items, each with `waiting` or `problem` and `askedAt`. `rankUrgent(items)` orders them: a problem before a question, an older question before a newer one.
- The rows, and the index of the one to raise: `applyUrgency(rows, index, "needs" | "problem")`. Pass `-1` when nothing needs you.

## Do and don't

- Do raise one thing only. The rest of the waiting items stay listed, in order, with their own signals.
- Do keep the order of the list stable; rising is a shift and a light, not a move to the top.
- Receding fades to `recede` (0.72) in Dark only, where text stays 4.5:1 or better. In Light and both high-contrast themes a fade would break contrast, so nothing fades and the rise alone carries it.

## Accessibility

- The rise repeats what the row's Signal already says; it adds no meaning a screen reader would miss.
- Under no motion the row does not shift sideways; the edge and light stay.
