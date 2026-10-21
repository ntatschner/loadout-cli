// Which page is showing, from the address's hash, so a reload and the back
// button keep the place and a link can be shared on this machine.

export type PageKey = 'home' | 'sessions' | 'ideas' | 'teams' | 'events' | 'memory' | 'context' | 'spend' | 'projects' | 'commands' | 'settings';

export interface Route {
  page: PageKey | 'session' | 'unknown';
  /** What follows the page: a session id, or a memory project, store and topic. */
  rest: string[];
  /**
   * What follows a '?', left as it was written: the Events page's filter,
   * in the same canonical form 'loadout events --filter' takes, so a
   * filtered view is an address that can be bookmarked. Optional so that a
   * route built by hand, as tests build them, need not say it.
   */
  query?: string;
}

const PAGES: PageKey[] = ['home', 'sessions', 'ideas', 'teams', 'events', 'memory', 'context', 'spend', 'projects', 'commands', 'settings'];

export function parse(hash: string): Route {
  const bare = hash.replace(/^#\/?/, '');
  const mark = bare.indexOf('?');
  const query = mark < 0 ? '' : bare.slice(mark + 1);
  const parts = (mark < 0 ? bare : bare.slice(0, mark)).split('/').filter(Boolean).map(decodeURIComponent);
  const [first = 'home', ...rest] = parts;

  if (first === 'session' && rest.length) {
    return { page: 'session', rest, query };
  }

  return { page: (PAGES as string[]).includes(first) ? (first as PageKey) : 'unknown', rest, query };
}

/** The address for a page and what follows it, with a query after a '?' where there is one. */
export function hashOf(parts: string[], query = ''): string {
  return `#${parts.map(encodeURIComponent).join('/')}${query ? `?${query}` : ''}`;
}

class Router {
  route = $state<Route>(parse(typeof location === 'undefined' ? '' : location.hash));

  constructor() {
    if (typeof window !== 'undefined') {
      window.addEventListener('hashchange', () => {
        this.route = parse(location.hash);
        // To the top of the new page, with focus on it, as a page load would.
        window.scrollTo(0, 0);
        document.getElementById('main')?.focus({ preventScroll: true });
      });
    }
  }

  /**
   * Changes the address without leaving the place: no history entry, no
   * scroll to the top. For something opened over a page, such as one task's
   * dialog, which a reload should reopen but which is not a page of its own.
   */
  replace(...parts: string[]): void {
    this.replaceWith(parts);
  }

  /** As replace, with a query: how the Events page keeps its filter in the address as it changes. */
  replaceWith(parts: string[], query = ''): void {
    const hash = hashOf(parts, query);

    history.replaceState(history.state, '', hash);
    this.route = parse(hash);
  }

  go(...parts: string[]): void {
    const hash = `#${parts.map(encodeURIComponent).join('/')}`;

    if (location.hash === hash) {
      this.route = parse(hash);
    } else {
      location.hash = hash;
    }
  }
}

export const router = new Router();
