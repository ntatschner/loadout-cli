// Arrow keys, Home and End move between the items of a tablist or radiogroup
// and pick as they go, with one tab stop for the group.

/** The index a key moves to from `index` among `count` items, or null for a key that does not move. */
export function rovingIndex(key: string, index: number, count: number): number | null {
  const step: Record<string, number> = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 };

  if (key === 'Home') {
    return 0;
  }

  if (key === 'End') {
    return count - 1;
  }

  if (!(key in step)) {
    return null;
  }

  return (index + step[key] + count) % count;
}

/** Handles a keydown on item `index`: picks the next one and focuses it. */
export function roving(event: KeyboardEvent, index: number, items: (HTMLElement | undefined)[], pick: (index: number) => void): void {
  const next = rovingIndex(event.key, index, items.length);

  if (next === null) {
    return;
  }

  event.preventDefault();
  pick(next);
  items[next]?.focus();
}
