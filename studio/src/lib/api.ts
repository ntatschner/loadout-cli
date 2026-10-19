// The daemon's API, as Studio reads and changes it. Studio signs in with the
// cookie 'loadout dashboard --open' gives it, so requests carry no
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

/** A refusal in words: the reason where the body is JSON carrying one, as several of the daemon's routes answer, otherwise the body. */
function reasonOf(body: string): string {
  try {
    const read = JSON.parse(body) as { error?: unknown };

    return typeof read?.error === 'string' && read.error ? read.error : body;
  } catch {
    return body;
  }
}

async function call<T>(method: string, path: string, body?: unknown): Promise<T> {
  const response = await send(method, path, body);

  if (!response.ok) {
    throw new Refused(reasonOf(await response.text()) || `${method} ${path} answered ${response.status}.`, response.status);
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
  /** Whether the accessibility profile says a screen reader is in use. */
  screenReader?: boolean;
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
  /** Each list's saved query, by view; null where nothing is saved and the list opens with its default. */
  views?: Record<string, string | null>;
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
  lastInputAt?: string | null;
  /** What the agent's own hooks say it needs, until somebody types: never read off the screen. */
  needs?: 'waiting' | 'approval' | null;
  /** The agent's own id for the conversation, which 'loadout resume' takes. */
  agentSession?: string | null;
  /** 'command' for a command that asks as it goes, 'launch' for an agent session. */
  kind?: 'launch' | 'command';
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
  /** A command that asks as it goes, run in the terminal instead of a launch. */
  command?: string;
  words?: string[];
}

export const sessions = {
  list: () => call<Session[]>('GET', '/api/v1/sessions'),
  start: (launch: Launch) => call<Session>('POST', '/api/v1/sessions', launch),
  end: (id: string) => call<void>('DELETE', `/api/v1/sessions/${encodeURIComponent(id)}`),
  /**
   * For a session this daemon does not host: whether an earlier daemon did,
   * and the agent's conversation id it left, for 'loadout resume'.
   */
  async gone(id: string): Promise<{ lost: boolean; agentSession: string | null }> {
    const response = await send('GET', `/api/v1/sessions/${encodeURIComponent(id)}`);

    if (response.status !== 410) {
      return { lost: false, agentSession: null };
    }

    const body = (await response.json()) as { agentSession?: string | null };

    return { lost: true, agentSession: body.agentSession ?? null };
  },
};

/* ---------- past conversations, and resuming one ---------- */

/** What a conversation's launch was given, where the agent's hook filed it under one. */
export interface LaunchedFor {
  launch: string;
  task: string | null;
  taskId: string | null;
  mode: string | null;
  profile: string | null;
  worktree: string | null;
}

/** A conversation from either agent's own history, as 'loadout sessions' lists it. */
export interface Conversation {
  agent: string;
  id: string;
  title: string;
  project: string | null;
  registered: boolean;
  directory: string;
  branch: string | null;
  lastActive: string;
  /** Null where there is no link: nothing is carried over, and nothing is guessed. */
  launchedFor: LaunchedFor | null;
  /** none, elsewhere, or hosted:<session id>. */
  running: string;
}

/** A session an earlier daemon hosted, ended when it stopped. */
export interface LostSession {
  id: string;
  endedAt: string;
  agentSession: string;
  conversation: Conversation | null;
}

export interface Resume {
  agentSession: string;
  agent?: string;
  /** The conversation's own folder, to register first; the daemon refuses any other. */
  registerFolder?: string;
  confirmRunning?: boolean;
  /** The lost session this carries on, to stop offering once it starts. */
  lost?: string;
  /** Carry nothing over: the sheet's fields are the whole of what it reopens with. */
  fresh?: boolean;
}

/** What a resume came to: started, or a question the page asks before trying again. */
export type Resumed =
  | { started: Session }
  | { conflict: 'hosted'; session: string }
  | { conflict: 'elsewhere' }
  | { conflict: 'unregistered'; directory: string };

export const conversations = {
  list: (filter: { project?: string; agent?: string; limit?: number } = {}) => {
    const query = new URLSearchParams();

    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== '') {
        query.set(key, String(value));
      }
    }

    const text = query.toString();

    return call<Conversation[]>('GET', `/api/v1/history${text ? `?${text}` : ''}`);
  },
  lost: () => call<LostSession[]>('GET', '/api/v1/history/lost'),
  forget: (id: string) => call<void>('DELETE', `/api/v1/history/lost/${encodeURIComponent(id)}`),
  /** Starts a hosted 'loadout resume'. The launch sheet's fields, where sent, are what it changes. */
  async resume(resume: Resume, sheet: Partial<Launch> = {}): Promise<Resumed> {
    const response = await send('POST', '/api/v1/sessions', { ...sheet, resume });

    if (response.status === 409) {
      return (await response.json()) as Resumed;
    }

    if (!response.ok) {
      throw new Refused(reasonOf(await response.text()) || `Resuming answered ${response.status}.`, response.status);
    }

    return { started: (await response.json()) as Session };
  },
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
  /** When nobody answers: what happens, and when. */
  timer?: { at: string; does: string } | null;
}

export interface RunNode {
  node: string;
  role: string;
  state: string;
  activity: string;
  doing: string | null;
  lastSeen: string | null;
  turns?: number;
  cost?: number;
  denials?: number;
  branch?: string | null;
  model?: string | null;
  base?: string | null;
  said?: string | null;
  tookSeconds?: number | null;
}

/** The numbers that change a decision about a run (RunMetrics). */
export interface RunNumbers {
  money: { spent: number; perMinute: number; rate: string; projected: number | null; budget: number | null; overrunning: boolean };
  rounds: { round: number; seconds: number; requests: number; running: boolean }[];
  nodes: { node: string; role: string; seconds: number; cost: number }[];
  trouble: { denials: number; rejected: number; retried: number; quietRounds: number; conflicts: number; any: boolean };
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
  coverage: { criterion: string; verdict: string; verdictInWords: string; met: boolean; because?: string | null; understood?: string | null; source?: string | null }[];
  gates: RunGate[];
  budget: { spent: number; cap: number | null; uncapped: boolean };
  nodes: RunNode[];
  attention: { kind: string; detail: string; clears: string }[];
  autonomy?: string;
  cost?: number;
  readings?: { criterion: string; reading: string }[];
  goalUnderstood?: string | null;
  merged?: string[];
  atMostRemainingSeconds?: number | null;
  numbers?: RunNumbers;
  turns?: { at: string; node: string; round: number; attempt: number; exchanges: number; cost: number; denials: number; completed: boolean; status: string | null; outcome: string | null }[];
}

export interface Runs {
  runs: Run[];
  acts: boolean;
  begins: boolean;
}

export const runs = {
  list: () => call<Runs>('GET', '/api/runs'),
  /** POST /api/runs/{run}/{verb}, the classic page's own route, which runs the command for the verb. */
  act: (run: string, verb: string, body: Record<string, string | boolean> = {}) =>
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

/* ---------- every command ---------- */

export type Effect = 'reads' | 'changes' | 'session' | 'terminalOnly';

export interface StudioArgument {
  name: string;
  position: number;
  required: boolean;
  many: boolean;
  description: string;
}

export interface StudioOption {
  name: string;
  short: string | null;
  kind: 'flag' | 'value' | 'values' | 'optional';
  valueName: string | null;
  description: string;
  required: boolean;
  global: boolean;
  choices: string[];
}

export interface StudioCommand {
  path: string;
  description: string;
  category: string;
  example: string;
  runnable: boolean;
  reach: { page: string; effect: Effect; reason: string | null; changesOnlyWith: string[] | null };
  arguments: StudioArgument[];
  options: StudioOption[];
}

export type JobState = 'running' | 'done' | 'failed' | 'cancelled';

export interface Job {
  id: string;
  path: string;
  line: string;
  state: JobState;
  exitCode: number | null;
  startedAt: string;
  endedAt: string | null;
  truncated: boolean;
  /** Only when one job is read on its own. */
  output: string | null;
  error: string | null;
}

/** The command line, run from the page as a child of the daemon's launcher. */
export const commands = {
  catalogue: () => call<StudioCommand[]>('GET', '/api/v1/commands'),
  jobs: () => call<Job[]>('GET', '/api/v1/jobs'),
  job: (id: string) => call<Job>('GET', `/api/v1/jobs/${encodeURIComponent(id)}`),
  run: (path: string, words: string[]) => call<Job>('POST', '/api/v1/jobs', { path, words }),
  cancel: (id: string) => call<void>('DELETE', `/api/v1/jobs/${encodeURIComponent(id)}`),
  /** Runs a command that only reads and waits for it, kept nowhere: how a page reads. */
  read: (path: string, words: string[]) => call<Job>('POST', '/api/v1/read', { path, words }),
};

/**
 * What a command that reads says with --json, parsed. A command that failed
 * is thrown as a refusal carrying what it said on standard error.
 */
export async function readJson<T>(path: string, words: string[] = []): Promise<T> {
  const read = await commands.read(path, [...words, '--json']);

  if (read.state !== 'done') {
    throw new Refused(read.error?.trim() || read.output?.trim() || `'loadout ${path}' ended with exit code ${read.exitCode}.`, 400);
  }

  return JSON.parse(read.output ?? 'null') as T;
}

/* ---------- Loadout's own settings ---------- */

export interface ConfigSetting {
  name: string;
  value: string;
  description: string;
  machineLocal: boolean;
  flag: boolean;
  whenUnset: string | null;
}

export interface ConfigGroup {
  group: string;
  settings: ConfigSetting[];
}

/** Every setting 'loadout config' knows, grouped as the terminal's Settings screen groups them. */
export const configuration = () => call<ConfigGroup[]>('GET', '/api/v1/config');

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

/* ---------- team runs, read and steered from the Teams pages ---------- */

export interface Choices {
  teams: { name: string; description: string; whose: string; template: boolean; trouble: string | null; nodes: number; autonomy: string; doneWhen: string[] | null; takeRecommendationAfter: string | null }[];
  projects: string[];
  here: string | null;
  projectDoneWhen: Record<string, { text: string; teams: string[] }[]>;
}

export interface StreamStep {
  at: string;
  kind: 'said' | 'tool' | 'answered' | 'refused' | 'started' | 'ended';
  tool: string | null;
  target: string | null;
  text: string | null;
  sub: boolean;
}

export interface RunDocument {
  name: string;
  kind: string;
  subject: string;
  bytes: number;
  written: string;
}

export interface NodeDiff {
  node: string;
  branch: string;
  from: string;
  merged: boolean;
  summary: string;
  patch: string;
  cut: boolean;
}

export interface Outcome {
  criterion: string;
  reading: string | null;
  verdict: string | null;
  met: boolean;
  because: string | null;
  source: string | null;
  delivered: { node: string; kind: string; at: string }[];
  unfound: string[];
}

/** A route that answers a refusal as JSON with its reason: the body is read whatever the status. */
async function answered<T extends { error: string | null }>(method: string, path: string, body: unknown): Promise<T> {
  const response = await send(method, path, body);
  const text = await response.text();

  try {
    return JSON.parse(text) as T;
  } catch {
    throw new Refused(text || `${method} ${path} answered ${response.status}.`, response.status);
  }
}

const runPath = (run: string, rest = '') => `/api/runs/${encodeURIComponent(run)}${rest}`;
const query = (pairs: Record<string, string>) => new URLSearchParams({ ...pairs, ...(printed ? { token: printed } : {}) }).toString();

export const teamApi = {
  choices: () => call<Choices>('GET', '/api/choices'),
  daemon: () => call<{ daemon: boolean; paused: boolean | null }>('GET', '/api/daemon'),
  bin: () => call<{ days: number; entries: { kind: 'run' | 'team'; name: string; team: string; binned: string; goes: string | null; unmerged: string[]; bytes: number }[] }>('GET', '/api/bin'),
  roles: () => call<{ roles: { role: string; model: string | null; runs: number; turns: number; cost: number; each: number; accepted: number; rejected: number; accepting: number | null; denials: number; refusals: number; seconds: number }[] }>('GET', '/api/roles'),
  /** Starts a run in the background: the command a sheet shows, watched for a few seconds so an early refusal comes back. */
  start: (body: Record<string, unknown>) => answered<{ started: boolean; error: string | null }>('POST', '/api/start', body),
  prune: (body: Record<string, unknown>) => answered<{ cleared: boolean; error: string | null }>('POST', '/api/prune', body),
  stream: (run: string, node: string, after = 0) => call<{ node: string; total: number; steps: StreamStep[] }>('GET', runPath(run, `/stream/${encodeURIComponent(node)}?after=${after}`)),
  spent: (run: string) => call<{ nodes: { node: string; role: string; thinking: number; tools: number; writing: number; idle: number }[] }>('GET', runPath(run, '/spent')),
  documents: (run: string) => call<{ documents: RunDocument[] }>('GET', runPath(run, '/documents')),
  document: (run: string, name: string) => call<{ name: string; kind: string; subject: string; text: string }>('GET', runPath(run, `/documents/${encodeURIComponent(name)}`)),
  diff: (run: string, node: string) => call<NodeDiff>('GET', runPath(run, `/diff/${encodeURIComponent(node)}`)),
  outcomes: (run: string) => call<Outcome[]>('GET', runPath(run, '/outcomes')),
  attachState: () => call<{ here: boolean; set: boolean; can: boolean }>('GET', '/api/attach/passphrase'),
  /** Swaps the passphrase for a 30-minute grant, held only by the page. */
  attach: (passphrase: string) => call<{ grant: string | null; minutes: number; error: string | null }>('POST', '/api/attach', { passphrase }),
  /** Types at a live node, behind the grant. */
  async say(run: string, node: string, message: string, grant: string): Promise<void> {
    const response = await fetch(runPath(run, '/say'), {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-Loadout-Attach': grant, ...(printed ? { 'X-Loadout-Token': printed } : {}) },
      body: JSON.stringify({ node, message }),
    });

    if (!response.ok) {
      throw new Refused((await response.text()) || 'It was not sent.', response.status);
    }
  },
  /** The journal as it is written: every line from the start, then each new one. */
  journal(run: string, line: (entry: { at: string; node: string | null; kind: string; said: string; what: string }) => void): () => void {
    const source = new EventSource(`${runPath(run, '/events')}${printed ? `?token=${encodeURIComponent(printed)}` : ''}`, { withCredentials: true });

    source.addEventListener('line', (event) => {
      try {
        line(JSON.parse((event as MessageEvent).data));
      } catch {
        // A line this page does not read: left out.
      }
    });

    return () => source.close();
  },
  /** Where a delivered file is read from: committed at a commit, or loose on disk now. */
  fileAddress: (run: string, file: { commit?: string; path: string; loose?: boolean }, download = false) =>
    runPath(run, `/file?${query({ ...(file.loose ? { loose: file.path } : { commit: file.commit ?? '', path: file.path }), ...(download ? { download: '1' } : {}) })}`),
  zipAddress: (run: string) => runPath(run, `/outbox.zip${printed ? `?token=${encodeURIComponent(printed)}` : ''}`),
};
