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
  /**
   * A page kept open beside the terminals while the address names one of
   * them: #session/<id>?beside=projects. Only the deck's own routes, Home,
   * Sessions and a session, carry one.
   */
  beside?: string[];
}

const PAGES: PageKey[] = ['home', 'sessions', 'ideas', 'teams', 'events', 'memory', 'context', 'spend', 'projects', 'commands', 'settings'];

const pathOf = (text: string) => text.split('/').filter(Boolean).map(decodeURIComponent);

export function parse(hash: string): Route {
  const bare = hash.replace(/^#\/?/, '');
  const mark = bare.indexOf('?');
  const query = mark < 0 ? '' : bare.slice(mark + 1);
  const parts = pathOf(mark < 0 ? bare : bare.slice(0, mark));
  const [first = 'home', ...rest] = parts;
  const page = first === 'session' && rest.length ? 'session' : (PAGES as string[]).includes(first) ? (first as PageKey) : 'unknown';
  const beside = (page === 'session' || page === 'home' || page === 'sessions') && query.startsWith('beside=') ? pathOf(query.slice('beside='.length)) : [];

  return beside.length ? { page, rest, query: '', beside } : { page, rest, query };
}

/** The query that keeps a page open beside the terminals. */
export function besideQuery(parts: string[] | undefined): string {
  return parts?.length ? `beside=${parts.map(encodeURIComponent).join('/')}` : '';
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
    this.goWith(parts);
  }

  /** As go, with a query: how a jump keeps a page open beside the terminals. */
  goWith(parts: string[], query = ''): void {
    const hash = hashOf(parts, query);

    if (location.hash === hash) {
      this.route = parse(hash);
    } else {
      location.hash = hash;
    }
  }
}

export const router = new Router();
