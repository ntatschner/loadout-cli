// Everything in flight, as Studio draws it: team runs, hosted sessions,
// ideas and tasks, each turned into one line with a stage, a state word and
// the facts beside it. Pure, so the mapping is tested without a daemon.
import type { Idea, Run, RunGate, Session, TaskList } from './api';
import type { Stage } from './signals';
import type { TubeEvent, TubeLine } from './tube';

export type WorkKind = 'team run' | 'session' | 'idea' | 'task';

export interface Work extends TubeLine {
  kind: WorkKind;
  /** The id a command names: the run, session, idea or task id. */
  ref: string;
  project: string | null;
  /** What it is asking, while it waits. */
  ask?: string;
  /** Minutes since the epoch at which it asked, for ranking. */
  askedAt?: number;
  /** A session whose agent is asking to be allowed something, rather than waiting for a message. */
  approval?: boolean;
  /** A session waiting on background work of its own: quiet, and not needing you. */
  background?: boolean;
  /** A failure that wants acknowledging, which ranks above a question. */
  problem?: boolean;
  mins?: number;
  cost?: number;
  cap?: number | null;
  gate?: RunGate;
  run?: Run;
  session?: Session;
  idea?: Idea;
  task?: TaskList['tasks'][number];
}

/** A line's code, 1 to 8, from its id, so it keeps its colour and shape from one poll to the next. */
export function codeOf(id: string): string {
  let hash = 0;

  for (const char of id) {
    hash = (hash * 31 + char.charCodeAt(0)) | 0;
  }

  return String((Math.abs(hash) % 8) + 1);
}

const minutesOf = (iso: string | null | undefined, now: number) =>
  iso ? Math.max(0, Math.round((now - Date.parse(iso)) / 60000)) : 0;

const askedAtOf = (iso: string | null | undefined) => (iso ? Math.round(Date.parse(iso) / 60000) : undefined);

export function fromRun(run: Run, now: number): Work {
  const gate = run.gates[0];
  const failed = !run.running && run.outcome === 'failed' ? `ended: ${run.ended ?? 'failed'}` : undefined;
  const stage: Stage = run.waitingForYou ? 3 : run.running ? 2 : run.outcome === 'done' ? 4 : failed ? 2 : 4;
  const met = run.coverage.filter((one) => one.met).length;

  return {
    id: `run:${run.id}`,
    code: codeOf(`run:${run.id}`),
    kind: 'team run',
    ref: run.id,
    project: run.project,
    title: run.goal || run.team,
    where: run.team,
    stage,
    waiting: run.waitingForYou && !!gate,
    failed,
    problem: !!failed,
    ask: gate?.question,
    askedAt: askedAtOf(gate?.at),
    gate,
    note: run.waitingForYou
      ? `round ${run.rounds} · ${gate?.node ?? 'the lead'} asks`
      : run.running
        ? `round ${run.rounds}${run.roundLimit ? ` of ${run.roundLimit}` : ''}${run.coverage.length ? ` · ${met} of ${run.coverage.length} met` : ''}`
        : (run.ended ?? 'ended'),
    mins: Math.round(run.elapsedSeconds / 60),
    cost: run.budget.spent,
    cap: run.budget.cap,
    checks: run.coverage.map((one) => [one.criterion, one.met ? 'met' : one.verdict === 'unmet' ? 'unmet' : 'pending'] as [string, string]),
    workers: run.nodes.map((node) => [node.node, node.activity || node.state] as [string, string]),
    events: gate ? [{ stage: 3, kind: 'asked', t: clockOf(gate.at), text: `${gate.node} asks: ${gate.question}` }] : [],
    quiet: 0,
    run,
  };
}

export function fromSession(session: Session, now: number): Work {
  const quiet = session.lastOutputAt ? minutesOf(session.lastOutputAt, now) : minutesOf(session.startedAt, now);
  const events: TubeEvent[] = [{ stage: 2, kind: 'started', t: clockOf(session.startedAt), text: `Started in ${session.project ?? 'a project'}` }];

  if (session.ended) {
    events.unshift({ stage: 4, kind: 'ended', text: `Ended${session.exitCode === null ? '' : ` with exit code ${session.exitCode}`}` });
  }

  return {
    id: `session:${session.id}`,
    code: codeOf(`session:${session.id}`),
    kind: 'session',
    ref: session.id,
    project: session.project,
    title: session.task || session.taskId || session.title,
    where: session.project ?? '',
    stage: session.ended ? 4 : 2,
    // From the agent's own hooks, so a session asking for you rises with
    // everything else that does.
    waiting: !session.ended && !!session.needs,
    ask: session.needs === 'approval'
      ? 'It is asking for approval. The choices are in its terminal; answer them there.'
      : session.needs === 'waiting' ? 'It has finished and is waiting for your next message.' : undefined,
    approval: !session.ended && session.needs === 'approval',
    background: !session.ended && !session.needs && !!session.background,
    quiet,
    note: [session.mode, session.agent].filter(Boolean).join(' · '),
    mins: minutesOf(session.startedAt, now),
    events,
    session,
  };
}

const IDEA_STAGE: Record<Idea['detail']['stage'], Stage> = { captured: 0, answering: 1, ready: 1, proposed: 1, accepted: 4 };

export function fromIdea(idea: Idea, busy: string[]): Work {
  const detail = idea.detail;
  const unanswered = detail.questions.filter((q) => !q.answer).length;
  const running = busy.includes(detail.id);

  return {
    id: `idea:${detail.id}`,
    code: codeOf(`idea:${detail.id}`),
    kind: 'idea',
    ref: detail.id,
    project: detail.project,
    title: idea.title,
    where: detail.project ?? 'every project',
    stage: running ? 2 : IDEA_STAGE[detail.stage],
    waiting: !running && detail.stage === 'answering' && unanswered > 0,
    ask: unanswered ? `${unanswered} question${unanswered === 1 ? '' : 's'} to answer` : undefined,
    askedAt: askedAtOf(detail.lastRound?.when),
    failed: detail.error ? detail.error : undefined,
    problem: !!detail.error,
    note: running ? 'a round is running' : detail.stage === 'proposed' ? 'a plan to read' : detail.stage,
    quiet: 0,
    idea,
  };
}

const TASK_STAGE: Record<string, Stage> = { open: 0, doing: 2, blocked: 2, done: 4 };

export function fromTask(task: TaskList['tasks'][number], project: string | null): Work {
  return {
    id: `task:${project ?? ''}:${task.id}`,
    code: codeOf(`task:${project ?? ''}:${task.id}`),
    kind: 'task',
    ref: task.id,
    project,
    title: task.title,
    where: project ?? 'every project',
    stage: TASK_STAGE[task.state] ?? 0,
    blocked: task.state === 'blocked' ? task.note || 'blocked' : undefined,
    note: task.state,
    quiet: 0,
    task,
  };
}

/** Everything in view, in the order the boards draw it: what is asking first, then by kind. */
export function linesOf(
  input: { runs: Run[]; sessions: Session[]; ideas: Idea[]; busy: string[]; tasks: TaskList[] },
  now = Date.now(),
): Work[] {
  const runs = input.runs.filter((run) => run.running || run.waitingForYou || minutesOf(run.finished, now) < 24 * 60).map((run) => fromRun(run, now));
  const sessions = input.sessions.map((session) => fromSession(session, now));
  const ideas = input.ideas.filter((idea) => idea.detail.stage !== 'accepted').map((idea) => fromIdea(idea, input.busy));
  const tasks = input.tasks.flatMap((list) =>
    list.tasks.filter((task) => task.state === 'doing' || task.state === 'blocked').map((task) => fromTask(task, list.project)),
  );

  // Codes in drawing order, so no two lines in view share a colour and a
  // shape until there are more than eight.
  return [...runs, ...sessions, ...ideas, ...tasks].map((line, index) => ({ ...line, code: String((index % 8) + 1) }));
}

/** What the filters left off, as a phrase: '4 ideas and 2 team runs'. Empty when nothing was. */
export function hiddenOf(all: Work[], drawn: Work[]): string {
  const shown = new Set(drawn.map((line) => line.id));
  const counts = new Map<WorkKind, number>();

  for (const line of all) {
    if (!shown.has(line.id)) {
      counts.set(line.kind, (counts.get(line.kind) ?? 0) + 1);
    }
  }

  const parts = [...counts].map(([kind, n]) => `${n} ${kind}${n === 1 ? '' : 's'}`);

  return parts.length > 1 ? `${parts.slice(0, -1).join(', ')} and ${parts[parts.length - 1]}` : (parts[0] ?? '');
}

function clockOf(iso: string | null | undefined): string | undefined {
  if (!iso) {
    return undefined;
  }

  const at = new Date(iso);

  return `${at.getHours()}:${String(at.getMinutes()).padStart(2, '0')}`;
}

export { clockOf };
