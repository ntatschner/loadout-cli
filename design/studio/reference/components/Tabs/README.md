# Tabs

Tabs switch between views of the same thing (tokens over a day, a week, a month); a line of light slides under the tab you are on.

## What you provide

- `tabs`: labels in sentence case. `label`: what the tabs choose between, for screen readers ("Tokens used over").
- `onSelect(index, panel)`: fill the panel.

## Do and don't

- Do keep tabs to one row of short labels.
- Don't use tabs for pages: the side rail does that.

## Accessibility

- `tablist`, `tab` and `tabpanel` roles; arrow keys, Home and End move and select; only the selected tab is in the tab order.
- The selected tab is told by `aria-selected`, its `ink-strong` text and the underline, not by colour alone.
