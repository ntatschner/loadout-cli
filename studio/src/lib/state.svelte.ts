// What Studio knows and does, in one place the pages read from. The daemon is
// the source of everything here: Studio keeps nothing of its own except what
// a browser keeps for itself (single-letter keys, Ambient fullscreen).
import * as api from './api';
import { command } from './commands';
import { clockOf, linesOf, type Work } from './lines';
import type { SignalFamily } from './signals';
import { setAccent, setDensity, setMotion, setTheme, preferredTheme } from './studio';
import { toasts } from './toasts.svelte';

export interface FeedEntry {
  t: string;
  text: string;
  family: SignalFamily;
  glyph: string;
}

const LOCAL_KEYS = 'studio-keys';
const LOCAL_FULLSCREEN = 'studio-ambient-fullscreen';

function readLocal(key: string, fallback: boolean): boolean {
  try {
    const kept = localStorage.getItem(key);

    return kept === null ? fallback : kept === 'true';
  } catch {
    return fallback;
  }
}

function writeLocal(key: string, value: boolean): void {
  try {
    localStorage.setItem(key, String(value));
  } catch {
    // Storage refused: the choice lasts this visit.
  }
}

export class Studio {
  about = $state<api.About | null>(null);
  prefs = $state<api.Preferences | null>(null);
  runs = $state<api.Run[]>([]);
  sessions = $state<api.Session[]>([]);
  ideas = $state<api.Idea[]>([]);
  busy = $state<string[]>([]);
  tasks = $state<api.TaskList[]>([]);
  /** The commands run from Studio while this daemon has been running, newest first. */
  jobs = $state<api.Job[]>([]);
  /** Every command, with its options, read once when a page first needs it. */
  catalogue = $state<api.StudioCommand[]>([]);
  #catalogue: Promise<void> | null = null;
  signedOut = $state(false);
  /** Why the daemon could not be read, while it cannot. */
  offline = $state<string | null>(null);
  now = $state(Date.now());
  keys = $state(readLocal(LOCAL_KEYS, true));
  fullscreen = $state(readLocal(LOCAL_FULLSCREEN, false));
  loaded = $state(false);
  /** What happened while this page was open, newest first. */
  feed = $state<FeedEntry[]>([]);

  lines: Work[] = $derived(linesOf({ runs: this.runs, sessions: this.sessions, ideas: this.ideas, busy: this.busy, tasks: this.tasks }, this.now));
  waiting: Work[] = $derived(this.lines.filter((line) => line.waiting));
  watchOnly = $derived(this.about?.watchOnly ?? false);
  idleAfter = $derived(this.prefs?.idleAfter ?? 10);

  /** What a refused action says, when Studio is only watching. */
  readonly watchOnlyReason = 'Studio is only watching: type the command in a terminal';

  #stop: (() => void) | null = null;
  #seen: Map<string, string> | null = null;

  async start(): Promise<void> {
    await this.#guard(async () => {
      if (!(await api.signedIn())) {
        throw new api.SignedOut();
      }

      this.about = await api.about();
      await this.loadPrefs();
      await Promise.all([this.loadRuns(), this.loadSessions(), this.loadIdeas(), this.loadTasks(), this.loadJobs()]);
    });

    this.loaded = true;
    this.#notice();

    if (this.signedOut) {
      return;
    }

    this.#stop = api.listen((what) => {
      if (what.includes('sessions')) {
        void this.#guard(() => this.loadSessions());
      }

      if (what.includes('runs')) {
        void this.#guard(() => this.loadRuns());
      }

      if (what.includes('jobs')) {
        void this.#guard(() => this.loadJobs());
      }
    });

    // Ideas and tasks are not on the event stream: they change from a
    // terminal as often as from here, and the files say nothing when they do.
    setInterval(() => {
      this.now = Date.now();
      void this.#guard(() => Promise.all([this.loadIdeas(), this.loadTasks()]).then(() => undefined));
    }, 15000);

    matchMedia?.('(prefers-color-scheme: light)').addEventListener?.('change', () => this.applyPrefs());
    matchMedia?.('(prefers-contrast: more)').addEventListener?.('change', () => this.applyPrefs());
  }

  stop(): void {
    this.#stop?.();
  }

  async #guard(work: () => Promise<unknown>): Promise<void> {
    try {
      await work();
      this.offline = null;
    } catch (error) {
      if (error instanceof api.SignedOut) {
        this.signedOut = true;
        this.#stop?.();
      } else if (error instanceof TypeError) {
        this.offline = 'Studio cannot reach the daemon. It may have stopped: loadout daemon status';
      } else {
        this.offline = (error as Error).message;
      }
    }
  }

  async loadPrefs(): Promise<void> {
    this.prefs = await api.preferences.read();
    this.applyPrefs();
  }

  /** Theme, accent, density and motion as attributes on <html>; no theme set follows the system. */
  applyPrefs(): void {
    const prefs = this.prefs;
    setTheme(prefs?.theme || preferredTheme());
    setAccent(prefs?.accent ?? 'cyan');
    setDensity(prefs?.density ?? 'comfortable');
    setMotion(prefs?.motion ?? 'full');
  }

  async loadRuns(): Promise<void> {
    this.runs = (await api.runs.list()).runs;
    this.now = Date.now();
    this.#notice();
  }

  async loadSessions(): Promise<void> {
    this.sessions = this.about?.hostsSessions === false ? [] : await api.sessions.list();
    this.now = Date.now();
    this.#notice();
  }

  /**
   * What changed since the last read, as the feed and the toasts say it: a
   * new question, a run or session that ended. The first read only sets the
   * baseline, so opening Studio does not announce everything already there.
   */
  #notice(): void {
    if (!this.loaded) {
      return;
    }

    const states = new Map<string, string>();

    for (const line of this.lines) {
      if (line.kind === 'team run' || line.kind === 'session') {
        states.set(line.id, line.waiting ? `asks:${line.gate?.id}` : line.stage === 4 || line.failed ? 'ended' : 'going');
      }
    }

    const before = this.#seen;
    this.#seen = states;

    if (!before) {
      return;
    }

    for (const line of this.lines) {
      const was = before.get(line.id);
      const is = states.get(line.id);

      if (!is || was === is) {
        continue;
      }

      if (is.startsWith('asks:')) {
        this.#log(`${line.where} asks: ${line.ask ?? 'a question'}`, 'needs', '?');
        this.#ask(line);
      } else if (is === 'ended' && was) {
        this.#log(`${line.title} ended`, line.failed ? 'bad' : 'good', line.failed ? '✕' : '✓');
        toasts.add({ level: line.failed ? 'problem' : 'done', text: `${line.title} ended${line.failed ? `: ${line.failed}` : ''}` });
      } else if (!was) {
        this.#log(`Started: ${line.title}`, 'moving', '▶');
      }
    }
  }

  #log(text: string, family: SignalFamily, glyph: string): void {
    this.feed = [{ t: clockOf(new Date().toISOString()) ?? '', text, family, glyph }, ...this.feed].slice(0, 8);
  }

  /** A Needs you toast for a question, answerable from the toast itself. */
  #ask(line: Work): void {
    const gate = line.gate;

    if (!gate) {
      return;
    }

    const answers = gate.options.length ? gate.options : ['yes', 'no'];

    toasts.add({
      level: 'needs',
      text: `${line.where} asks: ${gate.question}`,
      cmd: line.project ?? '',
      actions: this.watchOnly
        ? []
        : answers.map((answer, index) => ({
            label: answer,
            kind: index === 0 ? 'needs' : '',
            cmd: command.gate(line.ref, gate.id, answer),
            onpress: async () => {
              const worked = await this.answerGate(line, answer);

              return worked ? `You answered: ${answer}` : 'The answer was not taken';
            },
          })),
    });
  }

  async loadIdeas(): Promise<void> {
    const read = await api.ideas.list();
    this.ideas = read.ideas;
    this.busy = read.busy;
  }

  async loadTasks(): Promise<void> {
    this.tasks = (await api.tasks.list()).lists;
  }

  /** Reads the command catalogue once, however many pages ask. */
  loadCatalogue(): Promise<void> {
    this.#catalogue ??= api.commands.catalogue().then(
      (all) => {
        this.catalogue = all;
      },
      (error) => {
        this.#catalogue = null;
        throw error;
      },
    );

    return this.#catalogue;
  }

  /** One command from the catalogue, once it has been read. */
  command(path: string): api.StudioCommand | null {
    return this.catalogue.find((one) => one.path === path) ?? null;
  }

  /** Reads the jobs, and says when one that was running here has ended. */
  async loadJobs(): Promise<void> {
    if (!this.about?.isDaemon) {
      return;
    }

    const was = new Map(this.jobs.map((job) => [job.id, job.state]));

    this.jobs = await api.commands.jobs();

    for (const job of this.jobs) {
      if (was.get(job.id) === 'running' && job.state !== 'running') {
        toasts.add({
          level: job.state === 'done' ? 'done' : job.state === 'failed' ? 'problem' : 'info',
          text: job.state === 'done' ? `Finished: ${job.path}` : job.state === 'failed' ? `Failed: ${job.path}` : `Cancelled: ${job.path}`,
          cmd: `$ ${job.line}`,
        });
      }
    }
  }

  /* ---------- settings ---------- */

  async setPreference(key: string, value: string): Promise<void> {
    if (this.watchOnly) {
      toasts.add({ level: 'info', text: 'Studio is only watching, so the setting was not changed.', cmd: command.setting(key, value) });

      return;
    }

    await this.#act(
      () => api.preferences.set(key, value),
      `Saved ${key}`,
      command.setting(key, value),
      () => this.loadPrefs(),
      { quiet: true },
    );
  }

  setKeys(on: boolean): void {
    this.keys = on;
    writeLocal(LOCAL_KEYS, on);
  }

  setFullscreen(on: boolean): void {
    this.fullscreen = on;
    writeLocal(LOCAL_FULLSCREEN, on);
  }

  /* ---------- actions, each saying its command ---------- */

  /** Runs one action and says how it went. Returns whether it worked. */
  async #act(
    work: () => Promise<unknown>,
    done: string,
    cmd: string,
    then?: () => Promise<unknown>,
    options: { quiet?: boolean } = {},
  ): Promise<boolean> {
    try {
      await work();

      if (!options.quiet) {
        toasts.add({ level: 'done', text: done, cmd });
      }

      await then?.();

      return true;
    } catch (error) {
      if (error instanceof api.SignedOut) {
        this.signedOut = true;

        return false;
      }

      toasts.add({ level: 'problem', text: (error as Error).message, cmd });

      return false;
    }
  }

  answerGate(line: Work, answer: string): Promise<boolean> {
    const run = line.ref;
    const gate = line.gate?.id ?? null;

    return this.#act(
      () => api.runs.act(run, 'gate', { gate: gate ?? '', answer }),
      `You answered ${line.where}: ${answer}`,
      command.gate(run, gate, answer),
      () => this.loadRuns(),
    );
  }

  hold(line: Work): Promise<boolean> {
    return this.#act(() => api.runs.act(line.ref, 'pause'), `${line.where} holds after this round`, command.pause(line.ref), () => this.loadRuns());
  }

  carryOn(line: Work): Promise<boolean> {
    return this.#act(() => api.runs.act(line.ref, 'resume'), `${line.where} carries on`, command.resume(line.ref), () => this.loadRuns());
  }

  stopRun(line: Work): Promise<boolean> {
    return this.#act(() => api.runs.act(line.ref, 'stop'), `${line.where} was stopped`, command.stop(line.ref), () => this.loadRuns());
  }

  taskState(line: Work, state: string): Promise<boolean> {
    return this.#act(
      () => api.tasks.act({ verb: 'state', id: line.ref, project: line.project, state }),
      `${line.title}: ${state}`,
      command.task(line.ref, state, line.project),
      () => this.loadTasks(),
    );
  }

  refine(line: Work): Promise<boolean> {
    return this.#act(
      () => api.ideas.act({ verb: 'refine', id: line.ref, project: line.project }),
      `${line.title}: another round started`,
      command.refine(line.ref),
      () => this.loadIdeas(),
    );
  }

  async startSession(launch: api.Launch, shown: string): Promise<api.Session | null> {
    try {
      const started = await api.sessions.start(launch);
      toasts.add({ level: 'done', text: `Started in ${launch.project}`, cmd: shown });
      await this.loadSessions();

      return started;
    } catch (error) {
      if (error instanceof api.SignedOut) {
        this.signedOut = true;
      } else {
        toasts.add({ level: 'problem', text: (error as Error).message, cmd: shown });
      }

      return null;
    }
  }

  endSession(session: api.Session): Promise<boolean> {
    return this.#act(() => api.sessions.end(session.id), `Ended ${session.title}`, command.endSession(), () => this.loadSessions());
  }
}

export const studio = new Studio();
