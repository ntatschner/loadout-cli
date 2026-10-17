# CommandBar

CommandBar runs along the bottom of Pure Focus. Press `:` from anywhere (when you are not typing) and type.

- It searches three kinds of thing, and labels each row with its kind:
  - **action**: the picked thing's actions, the same as the ActionPane's;
  - **go**: places and items;
  - **command**: any loadout command in Studio's command catalogue, which opens its form, filled in.
- Every match shows what it runs, before you press Enter. Arrow keys move, Enter runs, Escape closes and returns to the list.
- A combobox: `aria-expanded`, `aria-controls` and `aria-activedescendant`, with the matches as a listbox above the bar.
