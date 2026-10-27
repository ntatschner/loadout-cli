// What the command palette shows for a query: every word typed must appear;
// a row whose label matches ranks above one that matches only in its facts,
// and a group whose best row matches its label moves up.

export interface PaletteRow {
  key: string;
  label: string;
  sub?: string;
  kind?: string;
  glyph?: string;
  need?: boolean;
  /** Keeps the palette open when chosen, for a row that changes what it shows. */
  stay?: boolean;
  run?: (row: PaletteRow) => void;
}

export interface PaletteGroup {
  name: string;
  items: PaletteRow[];
}

function matches(query: string, text: string): boolean {
  const lowered = text.toLowerCase();

  return query
    .toLowerCase()
    .split(/\s+/)
    .filter(Boolean)
    .every((word) => lowered.includes(word));
}

/** The groups and rows to show, in order, for what has been typed. */
export function paletteRows(groups: PaletteGroup[], query: string): PaletteGroup[] {
  const level = (row: PaletteRow) =>
    !query ? 1 : matches(query, row.label) ? 2 : matches(query, `${row.label} ${row.sub ?? ''} ${row.kind ?? ''}`) ? 1 : 0;

  const ranked = groups
    .map((group) => {
      const items = group.items
        .map((row) => ({ row, level: level(row) }))
        .filter((one) => one.level > 0)
        .sort((a, b) => b.level - a.level);

      return { name: group.name, items: items.map((one) => one.row), best: items.length ? items[0].level : 0 };
    })
    .filter((group) => group.items.length);

  if (query) {
    ranked.sort((a, b) => b.best - a.best);
  }

  return ranked.map(({ name, items }) => ({ name, items }));
}
