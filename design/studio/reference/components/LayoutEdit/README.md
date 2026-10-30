# LayoutEdit

LayoutEdit is Edit layout for any page and any mode: the page's sections in the order you gave them, some hidden, edited in place and kept in Loadout's config so a layout follows you to your other machines. It is main's Home edit layout (the Lens and the Map) made general: give it the section ids, their names, the default order and the saved value, and it draws the sections, an "Edit layout" button for the page head, and the edit bar. Generalised from main's `studio/src/pages/Home.svelte`, `pages/Panel.svelte` and `lib/layout.ts` as built.

## What it shows

- **The sections**, each a `section.panel` with its heading (`ls-zone`, numbered `01` `02` when asked, with a count when given), in the saved order, hidden ones left out.
- **Edit layout**, a small HaloButton for the page head; it reads Done while editing.
- **The edit bar**, for under the head while editing: "Editing the Lens layout. Drag a section by its name, or use its arrows.", an Add back button for each hidden section, Reset (back to the default), Done, and the line that keeps it: `Kept in Loadout's config: $ loadout config set studio-layouts '…'`.
- **While editing**, each section gains its chrome: a grip to drag it by its name, the arrows ← ↑ ↓ → to move it earlier or later, and Hide. A section that holds live work keeps its arrows, but Hide is unavailable and says why: "holds live work, so it stays while it has something in it".

## What you provide

- `page`, and `surface` where a page has more than one ("home" and "lens"): the key is `<page>` or `<page>/<surface>`.
- `mode`: each mode keeps its own layout of the same page ("default", "terminals", "teams", "minimal", "pure").
- `sections` (ids with names, in the default order), or ids with `names`; `order` where the default is another order.
- `value`: `studio-layouts` as Loadout has it, the whole object. Its shape is `{"<page>[/<surface>]": {"<mode>": {"order": […], "hidden": […]}}}`. `""`, for the whole setting or a missing entry, is the default; a layout put back as it came takes its entry out, and with nothing left the setting is `""` again, so a reset unsets it.
- `legacy`: Home's old `studio-layout-lens` or `studio-layout-map`, read until `studio-layouts` is written.
- `render(id)`: the section's body, drawn once (`refresh(id)` draws it again). `count(id)`, `numbered`, `wide`, `quiet`, `class` for the container (Home's `lens`).
- `locked(id)`: true while the section has live work in it (Needs you with something waiting, Running with something running). It can be moved but not hidden, and is shown even if it was hidden before.
- `hideOnly` for Minimal, which stays one column: Hide and Add back, no moving.
- `onchange(key, value, command)`: called with `"studio-layouts"`, the whole new value, and the exact line, shell-quoted: `$ loadout config set studio-layouts '{"home/lens":{"default":{"order":[…],"hidden":["signal"]}}}'`.
- `disabledReason` where nothing can be kept (only watching): Edit layout shows it in place of a command.
- The pure helpers, as main's `lib/layout.ts` has them: `LayoutEdit.readLayout`, `move`, `place`, `hide`, `show` and `saved`; and `keyOf`, `read`, `write` and `command` for the whole setting.

## Do and don't

- Do put `button` in the page head and `bar` right under it, as Home does.
- Do pass `locked` for every section that lists live work. Live work is never hidden.
- Don't keep sizes here: the splits, the deck's slots and the desk's widths fit the screen they were made on, and stay in the browser.
- Don't write a layout that is the default: `saved` gives `""` for it.

## Accessibility

- Each section is a `section` named by its heading; Home's Map headings are for screen readers only outside editing (`quiet`), as main has them.
- Moving is never by drag alone: the arrows are real buttons, named "Move Focus earlier", "Move Focus up"…, and the first and last are disabled where they would do nothing. Focus stays on the arrow you pressed as the section moves.
- The edit bar is a region named "Editing the layout". An unavailable Hide is `aria-disabled` with its reason beside it, read as its description.
- In high contrast the editing outline and the bar's edge are 2px; there is no motion.
