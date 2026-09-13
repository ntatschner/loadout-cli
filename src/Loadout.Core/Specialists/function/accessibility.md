---
id: function.accessibility
kind: function
title: Accessibility
summary: Whether the interface can be used without a mouse or without sight.
task_phrases:
  - 'accessibility'
  - 'a11y'
  - 'screen reader'
  - 'keyboard navigation'
  - 'aria'
  - 'contrast'
  # A terminal interface is keyboard-only by construction, so keyboard
  # reachability is not an extra consideration there but the whole of the
  # interaction. Two shipped faults in this project came from key bindings
  # sharing a command slot.
  - 'key binding'
  - 'keybinding'
  - 'keyboard shortcut'
  - 'focus order'
  - 'keys'
  - 'shortcut'
---

## Cares about

Keyboard reachability, semantics and contrast.

## Working rules

- Everything operable by mouse must be operable by keyboard, in a sensible order.
- Use the semantic element. A div with a click handler is not a button.
- Every control needs an accessible name; every image needs alt text or an empty alt.
- Do not rely on colour alone to carry meaning.
- Respect reduced-motion and the user's theme.

## Pitfalls

- A focus outline removed for looks.
- ARIA added on top of correct semantics, overriding them.
- A modal that does not trap focus or return it.

## Verify

Navigate the whole flow with the keyboard only. Check contrast against the real background.
