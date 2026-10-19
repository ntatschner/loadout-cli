// Global search for the command palette: 'loadout find', which searches
// tasks, ideas, sessions, team runs and projects with the query every list
// takes, and each hit as a palette row that goes to where it is shown.
import { readJson } from './api';
import type { PaletteGroup, PaletteRow } from './palette';

export interface FoundHit {
  id: string;
  title: string;
  project: string | null;
  state: string | null;
  updated: string | null;
  open: string;
}

export interface Found {
  groups: { kind: string; hits: FoundHit[]; more: number }[];
}

const NAMES: Record<string, string> = { task: 'Tasks', idea: 'Ideas', session: 'Sessions', run: 'Team runs', project: 'Projects' };

type Go = (page: string, ...rest: string[]) => void;

/** Where a hit is shown in Studio. */
export function goFor(kind: string, hit: FoundHit, go: Go): () => void {
  switch (kind) {
    case 'task':
      return () => go('projects', hit.project ?? '+tasks');
    case 'idea':
      return () => go('ideas', hit.project ?? '~', hit.id);
    case 'run':
      return () => go('teams', hit.id);
    case 'project':
      return () => go('projects', hit.id);
    default:
      return () => go('sessions');
  }
}

/** What 'find' found, as palette groups after the palette's own. */
export function foundGroups(found: Found, go: Go): PaletteGroup[] {
  return found.groups.map((group) => ({
    name: `${NAMES[group.kind] ?? group.kind} found`,
    items: group.hits.map(
      (hit): PaletteRow => ({
        key: `find-${group.kind}-${hit.project ?? '~'}-${hit.id}`,
        glyph: '⌕',
        label: hit.title,
        sub: [hit.state, hit.project].filter(Boolean).join(' · '),
        kind: group.kind,
        run: goFor(group.kind, hit, go),
      }),
    ),
  }));
}

/** Searches everything for the palette. A query 'find' refuses finds nothing. */
export async function search(query: string, go: Go): Promise<PaletteGroup[]> {
  try {
    return foundGroups(await readJson<Found>('find', [`--query=${query}`, '--limit', '5']), go);
  } catch {
    return [];
  }
}
