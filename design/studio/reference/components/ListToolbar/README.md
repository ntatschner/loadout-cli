# ListToolbar

ListToolbar is the same search, filter and sort controls over every list. The box takes the query typed; the chips are its pieces as the daemon read them, and a chip's × takes that piece away; the menus add a filter or choose a sort. All of them edit the one query string, so they cannot disagree. '/' puts you in the box from anywhere on the page that is not already a field. Main puts it over the runs (Teams), ideas, projects, tasks and conversations. Ported from main's `studio/src/lib/components/ListToolbar.svelte` as built.

## What it shows

- **Find tasks**: the box, in mono, with an example from the list's own fields as its placeholder ("Type to filter, or state:open sort:-updated").
- **Add a filter…**: each choice field's choices, "is" and "is not", grouped by field.
- **Sort by**: the default order, then each field ascending and descending.
- **What the daemon refused**, under the box, when it did.
- **The chips**: each piece of the query, a sort read as "sorted by updated, descending", each with ×; and **Reset to default** when the query is not the list's default.
- **Showing 2 of 7.**, "Nothing matches." when none, and **Show all 7**, when the query hid some.

## What you provide

- `noun`, `query` and `shape` (the daemon's reading: `parts`, `fields`, `defaultQuery`).
- `shown` and `recorded` when it filtered, and `problem` when it refused.
- `onchange(query)`: read the list with it and call `set` with what came back. It fires on Enter, after a 350ms pause in typing, and on every chip, menu, reset and Show all.
- `slash: false` on all but one toolbar on a page. `destroy()` when it goes.

## Do and don't

- Do let the daemon parse the query: the toolbar only joins pieces, so chips and box always say the same.
- Do keep one toolbar per list, over it.
- Don't read the list on each keystroke: the pause is there for that.
- Escape puts back the query as it stands, without reading.

## Accessibility

- A `role="search"` region named "Filter and sort the tasks"; the box is labelled and has `aria-keyshortcuts="/"`.
- A refused query marks the box `aria-invalid` and is an alert tied to it with `aria-describedby`.
- Each chip's × is named "Remove login"; the count is a `role="status"` line.
