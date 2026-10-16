// The daemon's API, as Studio reads and changes it. Studio signs in with the
// cookie 'loadout dashboard --open --studio' gives it, so requests carry no
// token; an address printed with one still works, and the token is then kept
// in memory only and taken out of the address bar at once.

const printed = new URLSearchParams(location.search).get('token') ?? '';

if (printed) {
  const clean = new URL(location.href);
  clean.searchParams.delete('token');
  history.replaceState(history.state, '', clean);
}

/** Thrown when the daemon does not know this browser: it needs signing in. */
export class SignedOut extends Error {
  constructor() {
    super('This browser is not signed in to Studio.');
  }
}

/** Thrown when the daemon refused, with its own words. */
export class Refused extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
  }
}

async function send(method: string, path: string, body?: unknown): Promise<Response> {
  const headers: Record<string, string> = {};

  if (printed) {
    headers['X-Loadout-Token'] = printed;
  }

  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  const response = await fetch(path, {
    method,
    credentials: 'same-origin',
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  // Only a refusal about the credential is signing out. The daemon also
  // answers 403 for watch-only, for another machine and for a store that is
  // read-only, and each of those says what it is.
  if (response.status === 403 && (await response.clone().text()).includes('needs the token')) {
    throw new SignedOut();
  }

  return response;
}

async function call<T>(method: string, path: string, body?: unknown): Promise<T> {
  const response = await send(method, path, body);

  if (!response.ok) {
    throw new Refused((await response.text()) || `${method} ${path} answered ${response.status}.`, response.status);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const type = response.headers.get('Content-Type') ?? '';

  return (type.includes('json') ? await response.json() : await response.text()) as T;
}

/* ---------- where Studio is ---------- */

export interface About {
  version: string;
  platform: string;
  port: number;
  watchOnly: boolean;
  isDaemon: boolean;
  hostsSessions: boolean;
  startsSessions: boolean;
  pseudoTerminal: string | null;
  attachPassphrase: boolean;
}

export const about = () => call<About>('GET', '/api/v1/about');

/** Whether the daemon knows this browser, asked without being refused. */
export const signedIn = async () => (await call<{ signedIn: boolean }>('GET', '/api/v1/signed-in')).signedIn;

/** Signs every browser out, this one included: 'loadout dashboard sign-out'. */
export const signOut = () => call<void>('POST', '/api/v1/sign-out');

/* ---------- Studio's settings ---------- */

export interface Preferences {
  halo: 'orbit' | 'gyroscope';
  theme: string | null;
  accent: string;
  density: string;
  motion: string;
  idleAfter: number;
  layoutLens: string | null;
  layoutMap: string | null;
  panels: { lens: string[]; map: string[] };
}

export const preferences = {
  read: () => call<Preferences>('GET', '/api/v1/preferences'),
  /** Runs 'loadout config set <key> <value>'. An empty value unsets it. */
  set: (key: string, value: string) => call<string>('PUT', '/api/v1/preferences', { key, value }),
};

/* ---------- hosted sessions ---------- */

export interface Session {
  id: string;
  title: string;
  startedAt: string;
  ended: boolean;
  exitCode: number | null;
  viewers: number;
  columns: number;
  rows: number;
  terminal: string;
  memory: number | null;
  project: string | null;
  agent: string | null;
  mode: string | null;
  task: string | null;
  taskId: string | null;
  command: string | null;
  lastOutputAt: string | null;
}

export interface Launch {
  project: string;
  agent?: string;
  task?: string;
  taskId?: string;
  mode?: string;
  profile?: string;
  worktree?: string;
  offline?: boolean;
  noSync?: boolean;
  handoff?: boolean;
  columns?: number;
  rows?: number;
}

export const sessions = {
  list: () => call<Session[]>('GET', '/api/v1/sessions'),
  start: (launch: Launch) => call<Session>('POST', '/api/v1/sessions', launch),
  end: (id: string) => call<void>('DELETE', `/api/v1/sessions/${encodeURIComponent(id)}`),
};

/** The WebSocket address of a session's terminal, on this page's own host. */
export function terminalAddress(session: Session): string {
  const scheme = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const query = printed ? `?token=${encodeURIComponent(printed)}` : '';

  return `${scheme}//${location.host}${session.terminal}${query}`;
}

/* ---------- the launch sheet ---------- */

export interface Sheet {
  projects: { slug: string; name: string; path: string | null; here: boolean }[];
  project: string | null;
  agent: string;
  defaultAgent: string | null;
  agents: { name: string; display: string; installed: boolean }[];
  modes: string[];
  profiles: { label: string; value: string | null }[];
  worktrees: { label: string; value: string | null }[];
  tasks: { id: string; title: string; state: string }[];
  budget: { tokenBudget: number; warnAtPercent: number };
}

export interface Preview {
  command: string | null;
  error: string | null;
  mode?: string;
  specialists?: { id: string; kind: string; reason: string; estimatedTokens: number }[];
  omitted?: { id: string; reason: string }[];
  budget?: { estimatedTokens: number; tokenBudget: number; warnAtPercent: number };
}

export const launchSheet = {
  read: (project?: string | null, agent?: string | null) => {
    const query = new URLSearchParams();

    if (project) {
      query.set('project', project);
    }

    if (agent) {
      query.set('agent', agent);
    }

    return call<Sheet>('GET', `/api/v1/launch${query.size ? `?${query}` : ''}`);
  },
  preview: (launch: Launch) => call<Preview>('POST', '/api/v1/launch/preview', launch),
};

/* ---------- team runs, ideas and tasks: the classic dashboard's own routes ---------- */

export interface RunGate {
  id: string;
  kind: string;
  node: string;
  role: string;
  question: string;
  options: string[];
  recommendation: string | null;
  at: string;
  until: string | null;
}

export interface RunNode {
  node: string;
  role: string;
  state: string;
  activity: string;
  doing: string | null;
  lastSeen: string | null;
}

export interface Run {
  id: string;
  team: string;
  goal: string;
  running: boolean;
  waitingForYou: boolean;
  ended: string | null;
  outcome: string | null;
  project: string | null;
  started: string;
  finished: string | null;
  rounds: number;
  roundLimit: number;
  elapsedSeconds: number;
  coverage: { criterion: string; verdict: string; verdictInWords: string; met: boolean }[];
  gates: RunGate[];
  budget: { spent: number; cap: number | null; uncapped: boolean };
  nodes: RunNode[];
  attention: { kind: string; detail: string; clears: string }[];
}

export interface Runs {
  runs: Run[];
  acts: boolean;
  begins: boolean;
}

export const runs = {
  list: () => call<Runs>('GET', '/api/runs'),
  /** POST /api/runs/{run}/{verb}, the classic page's own route, which runs the command for the verb. */
  act: (run: string, verb: string, body: Record<string, string> = {}) =>
    call<string>('POST', `/api/runs/${encodeURIComponent(run)}/${verb}`, body),
};

export interface Idea {
  title: string;
  detail: {
    id: string;
    project: string | null;
    stage: 'captured' | 'answering' | 'ready' | 'proposed' | 'accepted';
    captured: string;
    error: string | null;
    lastRound: { when: string; agent: string | null } | null;
    questions: { id: string; question: string; answer: string | null }[];
  };
}

export const ideas = {
  list: () => call<{ ideas: Idea[]; busy: string[] }>('GET', '/api/ideas'),
  act: (body: Record<string, unknown>) => call<string>('POST', '/api/ideas', body),
};

export interface TaskList {
  project: string | null;
  tasks: { id: string; title: string; state: string; note: string | null; declared: string }[];
}

export const tasks = {
  list: () => call<{ lists: TaskList[]; team: string }>('GET', '/api/tasks'),
  act: (body: Record<string, unknown>) => call<string>('POST', '/api/tasks', body),
};

/* ---------- memory ---------- */

export interface Topic {
  name: string;
  scope: string;
  description: string;
  kind: string;
  written: string;
  version: string;
  text: string;
}

const topicPath = (slug: string, scope: string, name: string) =>
  `/api/v1/projects/${encodeURIComponent(slug)}/memory/${encodeURIComponent(scope)}/${encodeURIComponent(name)}`;

export const memory = {
  projects: () => call<{ slug: string; name: string }[]>('GET', '/api/v1/projects'),
  topics: (slug: string) => call<{ project: string; topics: (Omit<Topic, 'text' | 'version'> & { facts: number; editable: boolean })[]; agent: { source: string; command: string; wouldImport?: string[]; drifted?: string[]; error?: string } | null }>('GET', `/api/v1/projects/${encodeURIComponent(slug)}/memory`),
  read: (slug: string, scope: string, name: string) => call<Topic>('GET', topicPath(slug, scope, name)),
  /** Saves an edit: { saved } with the topic as it now is, or { changed } with what somebody else wrote first. */
  async save(slug: string, scope: string, name: string, text: string, version: string): Promise<{ saved?: Topic; changed?: Topic }> {
    const response = await send('PUT', topicPath(slug, scope, name), { text, version });

    if (response.status === 409) {
      return { changed: (await response.json()) as Topic };
    }

    if (!response.ok) {
      throw new Refused((await response.text()) || `The save answered ${response.status}.`, response.status);
    }

    return { saved: (await response.json()) as Topic };
  },
};

/* ---------- live ---------- */

/** Calls back with what changed, as the daemon says it. Returns a function that stops listening. */
export function listen(changed: (what: string[]) => void): () => void {
  // An event stream cannot send a header, so a printed token goes in its
  // address, as the terminal's does.
  const query = printed ? `?token=${encodeURIComponent(printed)}` : '';
  const source = new EventSource(`/api/v1/events${query}`, { withCredentials: true });

  source.addEventListener('changed', (event) => {
    try {
      changed(JSON.parse((event as MessageEvent).data) as string[]);
    } catch {
      // Something the daemon said that this page does not read: ignored.
    }
  });

  return () => source.close();
}
