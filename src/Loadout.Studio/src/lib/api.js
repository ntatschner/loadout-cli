/**
 * The daemon's versioned API. A window signs in with a cookie; one that is
 * refused means the cookie is missing or has been signed out.
 */

// An address the daemon printed carries its token, as the classic page's
// does. It is read once, kept in memory only, and taken out of the address
// bar at once so it is not left in history or a bookmark. A window opened
// from a sign-in link has none, and the cookie does the work instead.
const token = new URLSearchParams(location.search).get('token') ?? '';

if (token) {
  const clean = new URL(location.href);
  clean.searchParams.delete('token');
  history.replaceState(history.state, '', clean);
}

export class SignedOut extends Error {
  constructor() {
    super('Not signed in.');
  }
}

async function send(method, path, body) {
  const response = await fetch(path, {
    method,
    credentials: 'same-origin',
    headers: {
      ...(token ? { 'X-Loadout-Token': token } : {}),
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  // Only a refusal about the credential means signed out. The server also
  // answers 403 for a store that is read-only here and for a request from
  // another machine, and those say what they are.
  if (response.status === 403) {
    const said = await response.clone().text();

    if (said.includes('needs the token')) {
      throw new SignedOut();
    }
  }

  return response;
}

async function call(method, path, body) {
  const response = await send(method, path, body);

  if (!response.ok) {
    throw new Error((await response.text()) || `${method} ${path} answered ${response.status}.`);
  }

  return response.status === 204 ? null : response.json();
}

export const sessions = {
  list: () => call('GET', '/api/v1/sessions'),
  start: (launch) => call('POST', '/api/v1/sessions', launch),
  end: (id) => call('DELETE', `/api/v1/sessions/${encodeURIComponent(id)}`),
};

const topicPath = (slug, scope, name) =>
  `/api/v1/projects/${encodeURIComponent(slug)}/memory/${encodeURIComponent(scope)}/${encodeURIComponent(name)}`;

export const memory = {
  projects: () => call('GET', '/api/v1/projects'),
  topics: (slug) => call('GET', `/api/v1/projects/${encodeURIComponent(slug)}/memory`),
  read: (slug, scope, name) => call('GET', topicPath(slug, scope, name)),

  /**
   * Saves an edit. Answers { saved } with the topic as it now is, or
   * { changed } with what was found on disk instead, because somebody else
   * wrote it first.
   */
  async save(slug, scope, name, text, version) {
    const response = await send('PUT', topicPath(slug, scope, name), { text, version });

    if (response.status === 409) {
      return { changed: await response.json() };
    }

    if (!response.ok) {
      throw new Error((await response.text()) || `The save answered ${response.status}.`);
    }

    return { saved: await response.json() };
  },
};

/** The WebSocket address for a session's terminal, on this page's own host. */
export function terminalAddress(session) {
  const scheme = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const query = token ? `?token=${encodeURIComponent(token)}` : '';

  return `${scheme}//${location.host}${session.terminal}${query}`;
}
