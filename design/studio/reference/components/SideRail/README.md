# SideRail

SideRail is Studio's navigation: a narrow rail of page icons down the left edge, each with a live count, that widens over the page with a dim when you hover or tab into it; below 600px a Pages tab slides the same list in.

## What you provide

- `pages`: `{ key, name, fact, need, href }` for Home, Sessions, Ideas, Teams, Events, Memory, Context, Spend, Projects, Commands and Settings, in that order, as main's App.svelte lists them. Each has its own icon (Events a bell, Commands a prompt). `fact` is the live count in words ("2 waiting on you", "3 unread", "everything else"); `need: true` adds the amber dot and colours the fact. `href` defaults to `#<key>`.
- `current`: the key of the page you are on.
- `onSearch`: opens the CommandPalette. The search row says its shortcut, Ctrl K.
- `foot` (optional): what sits at the foot of the rail, such as Report a problem and Appearance, as `.ls-rl` rows. Give a function, `(inSlide) => node`, and it is drawn in the rail and again in the slid-in list.
- Give the page `padding-left: var(--rail-width)` so nothing sits under the collapsed rail.

## Do and don't

- Do keep the rail over the page when it widens. It slides over with a dim; it never pushes the page sideways.
- Do keep counts short and in words; they are announced with the page name.
- Don't add pages beyond these eleven. Anything else is found with the palette.

## Accessibility

- A `<nav>` labelled "Studio"; the current page has `aria-current="page"`, an accent bar and its icon lit, not colour alone.
- Names and counts are in the DOM while the rail is collapsed, so screen readers hear them; keyboard focus widens it.
- The Pages tab has `aria-expanded`; the slide-in moves focus to Close, and Escape or Close returns focus to the tab. Following a page, or Search, from the slid-in list puts it away.
- Rows are 52px tall.
