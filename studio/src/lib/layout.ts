// A view's layout as Loadout keeps it in studio-layout-lens and
// studio-layout-map: the order of its panels and which are hidden. The boards
// placed panels at pixel offsets on a 1440px board; Studio keeps the order
// instead and lets the grid place them, so the same layout works at 320px
// and at 200% zoom. The daemon checks the same shape (ConfigKeys.Layout).

export interface Layout {
  order: string[];
  hidden: string[];
}

export const LENS_PANELS = ['needs', 'focus', 'all', 'start', 'signal'] as const;
export const MAP_PANELS = ['status', 'map', 'strip'] as const;

export const PANEL_NAMES: Record<string, string> = {
  needs: 'Needs you',
  focus: 'Focus',
  all: 'Everything',
  start: 'Start a session',
  signal: 'Signal',
  status: 'Network status',
  map: 'The map',
  strip: 'The picked line',
};

/** The layout a saved value describes, mended: unknown panels dropped, missing ones added at the end. */
export function readLayout(saved: string | null | undefined, panels: readonly string[]): Layout {
  let parsed: Partial<Layout> = {};

  try {
    parsed = saved ? (JSON.parse(saved) as Partial<Layout>) : {};
  } catch {
    parsed = {};
  }

  const known = (list: unknown) => (Array.isArray(list) ? list.filter((one): one is string => typeof one === 'string' && panels.includes(one)) : []);
  const order = [...new Set(known(parsed.order))];

  for (const panel of panels) {
    if (!order.includes(panel)) {
      order.push(panel);
    }
  }

  return { order, hidden: [...new Set(known(parsed.hidden))] };
}

/** Moves a panel by `by` places among all of them, hidden ones included. */
export function move(layout: Layout, panel: string, by: number): Layout {
  const order = [...layout.order];
  const from = order.indexOf(panel);
  const to = Math.max(0, Math.min(order.length - 1, from + by));

  if (from < 0 || from === to) {
    return layout;
  }

  order.splice(from, 1);
  order.splice(to, 0, panel);

  return { ...layout, order };
}

/** Puts `panel` where `before` is, for a drop. */
export function place(layout: Layout, panel: string, before: string): Layout {
  if (panel === before) {
    return layout;
  }

  const order = layout.order.filter((one) => one !== panel);
  order.splice(Math.max(0, order.indexOf(before)), 0, panel);

  return { ...layout, order };
}

export const hide = (layout: Layout, panel: string): Layout => ({ ...layout, hidden: [...new Set([...layout.hidden, panel])] });

export const show = (layout: Layout, panel: string): Layout => ({ ...layout, hidden: layout.hidden.filter((one) => one !== panel) });

/** What Loadout keeps; the default layout keeps nothing, so a reset unsets the key. */
export function saved(layout: Layout, panels: readonly string[]): string {
  const plain = !layout.hidden.length && layout.order.every((one, index) => one === panels[index]);

  return plain ? '' : JSON.stringify({ order: layout.order, hidden: layout.hidden });
}
