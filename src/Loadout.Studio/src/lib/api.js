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

async function call(method, path, body) {
  const response = await fetch(path, {
    method,
    credentials: 'same-origin',
    headers: {
      ...(token ? { 'X-Loadout-Token': token } : {}),
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (response.status === 403) {
    throw new SignedOut();
  }

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

/** The WebSocket address for a session's terminal, on this page's own host. */
export function terminalAddress(session) {
  const scheme = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const query = token ? `?token=${encodeURIComponent(token)}` : '';

  return `${scheme}//${location.host}${session.terminal}${query}`;
}
