// What Studio knows and does, in one place the pages read from. The daemon is
// the source of everything here: Studio keeps nothing of its own except what
// a browser keeps for itself (single-letter keys, Ambient fullscreen).
import * as api from './api';
import { command } from './commands';
import { clockOf, linesOf, type Work } from './lines';
import type { SignalFamily } from './signals';
import { setAccent, setBackdrop, setDensity, setMotion, setTheme, preferredTheme } from './studio';
import { toasts } from './toasts.svelte';
import { isMode, morph, nextMode, type StudioMode } from './modes';
import { resumeConversation, type ResumeAsk, type ResumeOutcome } from './resume';
import { reloadsFor, type Reload } from './reloads';
import { resumeCommand } from './terminal';
import { reveal } from './reveal.svelte';

export interface FeedEntry {
  t: string;
  text: string;
  family: SignalFamily;
  glyph: string;
}

const LOCAL_KEYS = 'studio-keys';
const LOCAL_FULLSCREEN = 'studio-ambient-fullscreen';
const LOCAL_MODE = 'studio-mode';

/** A string this browser keeps, or the fallback where none is kept or storage is refused. */
function readText(key: string, fallback: string): string {
  try {
    return localStorage.getItem(key) ?? fallback;
  } catch {
    return fallback;
  }
}

function writeText(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Storage refused: the choice lasts this visit.
  }
}

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
  /** Past conversations from both agents, newest first: the history Sessions resumes from. */
  conversations = $state<api.Conversation[]>([]);
  /** Sessions an earlier daemon hosted, ended when it stopped, that can be carried on. */
  lost = $state<api.LostSession[]>([]);
  /** The question a resume is waiting on, for the one dialog that asks it. */
  resumeAsk = $state<{ question: ResumeAsk; answer: (yes: boolean) => void } | null>(null);
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
  /**
   * The mode, kept in this browser rather than Loadout's config, so a phone
   * can stay Minimal while a desktop stays in Terminals.
   */
  /** Something the ModeBar asked Minimal to open: Minimal's Home reads it and clears it. */
  focusing = $state<{ kind: 'session' | 'team run'; id: string } | null>(null);
  mode = $state<StudioMode>(((value) => (isMode(value) ? value : 'default'))(readText(LOCAL_MODE, 'default')));
  loaded = $state(false);
  /** What happened while this page was open, newest first. */
  feed = $state<FeedEntry[]>([]);
  /** Tokens billed as input over the last 30 days, once Spend has read them: the rail's Spend fact. */
  spentMonth = $state<number | null>(null);
  /** Unread events said to you that need you or went wrong, for the rail; null where the daemon keeps no history. */
  unreadEvents = $state<number | null>(null);
  /** Moved each time the history changes, so the Events page knows to read it again. */
  eventsChanged = $state(0);

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
      this.#version = this.about.version;
      await this.loadPrefs();
      await Promise.all([this.loadRuns(), this.loadSessions(), this.loadIdeas(), this.loadTasks(), this.loadJobs(), this.loadHistory(), this.loadEvents()]);
    });

    this.loaded = true;
    this.#notice();

    if (this.signedOut) {
      return;
    }

    const loads: Record<Reload, () => Promise<void>> = {
      sessions: () => this.loadSessions(),
      history: () => this.loadHistory(),
      runs: () => this.loadRuns(),
      jobs: () => this.loadJobs(),
      tasks: () => this.loadTasks(),
      ideas: () => this.loadIdeas(),
      eventlog: () => this.loadEvents(),
    };

    this.#stop = api.listen(
      (what) => {
        const reloads = reloadsFor(what);

        if (reloads.length > 0) {
          void this.#guard(() => Promise.all(reloads.map((reload) => loads[reload]())).then(() => undefined));
        }
      },
      () => void this.#back(),
    );

    void this.loadUpdate();

    // The clock moves every so often for the ages the page shows. The ideas
    // are read again with it, slowly, for what no file says: notes being
    // split into ideas, and a round whose process died without letting go.
    let ticks = 0;
    setInterval(() => {
      this.now = Date.now();

      if (++ticks % 4 === 0) {
        void this.#guard(() => this.loadIdeas());
      }

      // The daemon looks at the feed once a day; asking it every half hour
      // is enough for the badge to appear the same day.
      if (ticks % 120 === 0) {
        void this.loadUpdate();
      }
    }, 15000);

    matchMedia?.('(prefers-color-scheme: light)').addEventListener?.('change', () => this.applyPrefs());
    matchMedia?.('(prefers-contrast: more)').addEventListener?.('change', () => this.applyPrefs());
  }

  stop(): void {
    this.#stop?.();
  }

  /** What the daemon knows about updates, for the Settings badge; null until read, or from a dashboard. */
  update = $state<api.UpdateState | null>(null);

  /**
   * The version the daemon came back as, when it is not the one this page
   * was loaded from: this page's code is older than the daemon's, and a
   * reload gives the new one.
   */
  newVersion = $state<string | null>(null);

  #version: string | null = null;

  /** Reads the daemon's update state, quietly: a daemon that does not look leaves it null. */
  async loadUpdate(check = false): Promise<api.UpdateState | null> {
    try {
      this.update = await api.update(check);
    } catch {
      if (check) {
        throw new Error('The daemon could not look for updates.');
      }
    }

    return this.update;
  }

  /** The daemon is back after a restart: what it came back as, and the rest read again. */
  async #back(): Promise<void> {
    await this.#guard(async () => {
      const about = await api.about();

      this.about = about;

      if (this.#version && about.version !== this.#version) {
        this.newVersion = about.version;
      }

      await Promise.all([this.loadRuns(), this.loadSessions(), this.loadJobs(), this.loadUpdate()]);
    });
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

  /** Theme, accent, density, motion, backdrop and mode as attributes on <html>; no theme set follows the system. */
  applyPrefs(): void {
    const prefs = this.prefs;
    setTheme(prefs?.theme || preferredTheme());
    setAccent(prefs?.accent ?? 'cyan');
    setDensity(prefs?.density ?? 'comfortable');
    setMotion(prefs?.motion ?? 'full');
    setBackdrop(prefs?.backdrop ?? 'plain');
    // The mode is this browser's own, so it is set however the prefs read.
    document.documentElement.setAttribute('data-mode', this.mode);
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
        states.set(line.id, line.waiting ? `asks:${line.gate?.id ?? (line.approval ? 'approval' : 'waiting')}` : line.stage === 4 || line.failed ? 'ended' : 'going');
      }
    }

    const before = this.#seen;
    this.#seen = states;

    if (!before) {
      return;
    }

    // A question answered anywhere, here, on its page or in a terminal, takes
    // its Needs you toast with it rather than leaving it to be answered twice.
    for (const [id, toast] of this.#asked) {
      if (states.get(id)?.startsWith('asks:') !== true || states.get(id) !== this.#askedAs.get(id)) {
        toasts.settle(toast, 'Answered.');
        this.#asked.delete(id);
        this.#askedAs.delete(id);
      }
    }

    for (const line of this.lines) {
      const was = before.get(line.id);
      const is = states.get(line.id);

      if (!is || was === is) {
        continue;
      }

      if (is.startsWith('asks:')) {
        this.#askedAs.set(line.id, is);
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

  /** Each line's open Needs you toast, and the question it was raised for. */
  #asked = new Map<string, number>();
  #askedAs = new Map<string, string>();

  #log(text: string, family: SignalFamily, glyph: string): void {
    this.feed = [{ t: clockOf(new Date().toISOString()) ?? '', text, family, glyph }, ...this.feed].slice(0, 8);
  }

  /** A Needs you toast for a question, answerable from the toast itself. */
  #ask(line: Work): void {
    const gate = line.gate;

    // A session's agent says only that it is waiting, never what the
    // choices are, so the toast takes you to its terminal to answer there.
    if (line.kind === 'session') {
      this.#asked.set(line.id, toasts.add({
        level: 'needs',
        text: `${line.title} ${line.approval ? 'needs approval' : 'is waiting for you'}`,
        actions: [{ label: 'Go to the question', cmd: 'opens its terminal · changes nothing', onpress: () => reveal({ kind: 'session', id: line.ref }) }],
      }));

      return;
    }

    if (!gate) {
      return;
    }

    // A brief, the proposed done-when, readings and a budget are answered
    // with a box or a figure, which a toast has no room for: it goes to them.
    if (['brief', 'done-when', 'readings', 'budget'].includes(gate.kind)) {
      this.#asked.set(line.id, toasts.add({
        level: 'needs',
        text: `${line.where} asks: ${gate.brief ?? gate.question}`,
        actions: [{ label: 'Go to the question', cmd: 'opens the run · changes nothing', onpress: () => reveal({ kind: 'run', id: line.ref }) }],
      }));

      return;
    }

    const answers = gate.options.length ? gate.options : ['yes', 'no'];

    this.#asked.set(line.id, toasts.add({
      level: 'needs',
      // The words without the command, and the command on its own line below
      // them, so a toast that can say yes still shows what yes runs.
      text: `${line.where} asks: ${gate.brief ?? gate.question}`,
      cmd: gate.ask?.command ?? line.project ?? '',
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
    }));
  }

  /**
   * The unread count, and a nudge to the Events page. A daemon that keeps
   * no history, as a plain dashboard does not, leaves the count off rather
   * than saying Studio cannot reach it.
   */
  async loadEvents(): Promise<void> {
    try {
      this.unreadEvents = (await api.eventlog.unread()).unread;
    } catch (error) {
      if (error instanceof api.SignedOut) {
        throw error;
      }

      this.unreadEvents = null;
    }

    this.eventsChanged++;
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

  /** Changes mode as one animated step, and keeps it in this browser. */
  setMode(mode: StudioMode): void {
    if (mode === this.mode) {
      return;
    }

    morph(() => {
      this.mode = mode;
      document.documentElement.setAttribute('data-mode', mode);
      writeText(LOCAL_MODE, mode);
    });
  }

  /** Ctrl+Shift+M. */
  cycleMode(): void {
    this.setMode(nextMode(this.mode));
  }

  /** Something this browser keeps for a page: a layout, the last session. */
  kept(key: string, fallback: string): string {
    return readText(key, fallback);
  }

  keep(key: string, value: string): void {
    writeText(key, value);
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

  /**
   * Runs one action from a page, says how it went in a toast with the command
   * it ran, and reads the runs again. Returns whether it worked.
   */
  perform(work: () => Promise<unknown>, done: string, cmd: string, then: () => Promise<unknown> = () => this.loadRuns()): Promise<boolean> {
    if (this.watchOnly) {
      toasts.add({ level: 'info', text: 'Studio is only watching, so nothing was changed.', cmd });

      return Promise.resolve(false);
    }

    return this.#act(work, done, cmd, then);
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

  /** The history and what a restart left, from the daemon that keeps them; nothing anywhere else. */
  async loadHistory(): Promise<void> {
    if (this.about?.hostsSessions === false) {
      this.conversations = [];
      this.lost = [];

      return;
    }

    const [conversations, lost] = await Promise.all([api.conversations.list({ limit: 100 }), api.conversations.lost()]);
    this.conversations = conversations;
    this.lost = lost;
  }

  /**
   * Resumes a conversation in one of the daemon's terminals, asking first
   * where it has to. Returns the session to go to, or null for none.
   */
  async resume(conversation: api.Conversation, sheet: Partial<api.Launch> = {}, lost?: string, fresh = false): Promise<string | null> {
    let outcome: ResumeOutcome;

    try {
      outcome = await resumeConversation(
        conversation,
        {
          resume: (resume, asked) => api.conversations.resume(resume, asked),
          ask: (question) =>
            new Promise<boolean>((answer) => {
              // Cleared as it is answered, so the dialog closes then and not
              // when the resume it let through has finished starting.
              this.resumeAsk = {
                question,
                answer: (yes) => {
                  this.resumeAsk = null;
                  answer(yes);
                },
              };
            }),
        },
        sheet,
        lost,
        fresh,
      );
    } catch (error) {
      if (error instanceof api.SignedOut) {
        this.signedOut = true;
      } else {
        toasts.add({ level: 'problem', text: (error as Error).message, cmd: resumeCommand(conversation.id, conversation.project) });
      }

      return null;
    } finally {
      this.resumeAsk = null;
    }

    if ('declined' in outcome) {
      return null;
    }

    if ('goTo' in outcome) {
      return outcome.goTo;
    }

    toasts.add({ level: 'done', text: `Resumed in ${conversation.project ?? 'a new project'}`, cmd: resumeCommand(conversation.id, conversation.project) });
    await Promise.all([this.loadSessions(), this.loadHistory()]);

    return outcome.started.id;
  }

  /** Stops offering a session a restart ended, without resuming it. */
  forgetLost(lost: api.LostSession): Promise<boolean> {
    return this.#act(() => api.conversations.forget(lost.id), 'No longer offered', resumeCommand(lost.agentSession, lost.conversation?.project ?? null), () => this.loadHistory());
  }

  endSession(session: api.Session): Promise<boolean> {
    return this.#act(() => api.sessions.end(session.id), `Ended ${session.title}`, command.endSession(), () => this.loadSessions());
  }

  /**
   * Ends a session and leaves it listed, ended, as closing its tab does.
   * Quiet when it works: the session ending is announced like any other
   * that ends, and saying it twice is noise. A refusal still says so.
   */
  stopSession(session: api.Session): Promise<boolean> {
    return this.#act(() => api.sessions.stop(session.id), `Ended ${session.title}`, command.endHosted(session.id), () => this.loadSessions(), { quiet: true });
  }

  /** Renames a session for every browser, or gives it its launch name back for null. */
  renameSession(session: api.Session, name: string | null): Promise<boolean> {
    return this.#act(
      () => api.sessions.rename(session.id, name),
      name === null ? `Back to ${session.launchTitle ?? 'its launch name'}` : `Renamed to ${name}`,
      command.renameSession(session.id, name),
      () => this.loadSessions(),
    );
  }
}

export const studio = new Studio();
