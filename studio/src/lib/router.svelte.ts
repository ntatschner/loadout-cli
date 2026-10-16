// Which page is showing, from the address's hash, so a reload and the back
// button keep the place and a link can be shared on this machine.

export type PageKey = 'home' | 'sessions' | 'ideas' | 'teams' | 'memory' | 'context' | 'spend' | 'projects' | 'settings';

export interface Route {
  page: PageKey | 'session' | 'unknown';
  /** What follows the page: a session id, or a memory project, store and topic. */
  rest: string[];
}

const PAGES: PageKey[] = ['home', 'sessions', 'ideas', 'teams', 'memory', 'context', 'spend', 'projects', 'settings'];

export function parse(hash: string): Route {
  const parts = hash.replace(/^#\/?/, '').split('/').filter(Boolean).map(decodeURIComponent);
  const [first = 'home', ...rest] = parts;

  if (first === 'session' && rest.length) {
    return { page: 'session', rest };
  }

  return { page: (PAGES as string[]).includes(first) ? (first as PageKey) : 'unknown', rest };
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
