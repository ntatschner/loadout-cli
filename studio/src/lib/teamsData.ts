// A team run, as the daemon describes it, turned into what the Teams
// components draw: its state word, its summary, the reasons it wants you,
// its question as a Gate, its shape and its crew. Nothing here asks the
// daemon anything; the pages read and these only translate.
import type { Outcome, Run, RunGate } from './api';
import { gateKind, type GateShown, type NeedItem, type RunSummary } from './teams';
import type { BoardRun, BoardTeam } from './modes';

/** A run's state, as one of Studio's words. */
export function runWord(run: Run): string {
  if (run.waitingForYou) {
    return 'waiting for you';
  }

  if (run.running) {
    return 'running';
  }

  switch (run.outcome) {
    case 'done':
      return 'done';
    case 'failed':
      return 'failed';
    case 'limited':
      return 'limited';
    case 'blocked':
      return 'blocked';
    case 'needs-decision':
      return 'needs a decision';
    case 'stopped':
      return 'stopped';
    case 'abandoned':
      return 'abandoned';
    case 'stalled':
      return 'stalled';
    default:
      return run.ended ? 'stopped' : 'idle';
  }
}

/**
 * What the daemon can say of the process driving a run, as a badge, or null
 * when there is nothing to say. Liveness absent or unknown reads as live.
 */
export function livenessOf(run: Run): string | null {
  switch (run.liveness) {
    case 'not-responding':
      return run.owner ? `not responding · pid ${run.owner.pid}` : 'not responding';
    case 'possibly-not-responding':
      return 'possibly not responding';
    case 'unreadable':
      return 'owner unreadable';
    case 'silent':
      return run.owner ? `silent for an hour or more · pid ${run.owner.pid}` : 'silent for an hour or more';
    default:
      return null;
  }
}

/** Whether nothing is driving a run that still reads as running, so End run is all there is to do to it. */
export function unowned(run: Run): boolean {
  return run.running && (run.liveness === 'not-responding' || run.liveness === 'possibly-not-responding');
}

export function duration(seconds: number): string {
  if (seconds < 120) {
    return `${Math.round(seconds)}s`;
  }

  const minutes = Math.round(seconds / 60);

  return minutes < 120 ? `${minutes} min` : `${Math.floor(minutes / 60)}h ${minutes % 60} min`;
}

export function summaryOf(run: Run): RunSummary {
  return {
    id: run.id,
    goal: run.goal,
    team: run.team,
    project: run.project,
    posture: run.autonomy ?? null,
    word: runWord(run),
    round: run.rounds,
    rounds: run.roundLimit || undefined,
    elapsed: duration(run.elapsedSeconds),
    spent: run.budget.spent,
    cap: run.budget.cap,
    uncapped: run.budget.uncapped,
    projected: run.numbers?.money.projected ?? null,
    rate: run.numbers?.money.rate ?? null,
  };
}

/** When a gate asked, in words. */
function askedAt(at: string): string {
  const minutes = Math.max(0, Math.round((Date.now() - Date.parse(at)) / 60000));

  return minutes < 1 ? 'just now' : `${minutes} min ago`;
}

function timerWords(gate: RunGate): string | null {
  if (!gate.timer) {
    return null;
  }

  const minutes = Math.max(0, Math.round((Date.parse(gate.timer.at) - Date.now()) / 60000));

  return `${minutes} ${minutes === 1 ? 'minute' : 'minutes'} left, then it ${gate.timer.does === 'think again' ? 'goes back to the lead to think again' : 'takes the lead’s recommendation'}`;
}

/**
 * A run's question as a Gate draws it. What the daemon calls a kind is drawn
 * as that kind; one this does not know is answered yes or no.
 */
export function gateOf(run: Run, gate: RunGate): GateShown {
  const kind = gateKind(gate.kind);
  const base: GateShown = {
    kind,
    gate: gate.id,
    node: gate.node === 'run' || gate.node === 'lead' ? (gate.node === 'lead' ? 'the lead' : null) : gate.node,
    asked: askedAt(gate.at),
    question: gate.question,
    options: gate.options,
    recommendation: gate.recommendation,
    timer: timerWords(gate),
    ask: gate.ask ?? null,
  };

  switch (kind) {
    case 'readings':
      return { ...base, readings: (run.readings ?? []).map((one): [string, string] => [one.criterion, one.reading]) };
    case 'brief':
      return { ...base, brief: gate.recommendation ?? '' };
    case 'proposal':
      return {
        ...base,
        proposal: (gate.recommendation ?? '').split('\n').map((one) => one.trim()).filter(Boolean),
        held: run.coverage.filter((one) => one.source === 'project').map((one) => one.criterion),
      };
    default:
      return base;
  }
}

/** Every reason this run wants you, each clearing itself; its question answered in place. */
export function needsOf(run: Run, href: string, gateFor: (run: Run, gate: RunGate) => GateShown): NeedItem[] {
  const items: NeedItem[] = [];

  for (const gate of run.gates) {
    items.push({ reason: 'asked', run: run.id, goal: run.goal, team: run.team, href, gate: gateFor(run, gate) });
  }

  for (const one of run.attention) {
    if (one.kind === 'asking') {
      // Its question is already there, as a Gate, above.
      if (!run.gates.length) {
        items.push({ reason: 'asked', run: run.id, goal: run.goal, team: run.team, href, text: one.detail, clears: one.clears });
      }
    } else if (one.kind === 'goingnowhere') {
      items.push({ reason: 'silent', run: run.id, goal: run.goal, team: run.team, href, text: one.detail, clears: one.clears });
    } else if (one.kind === 'quiet') {
      items.push({ reason: 'quiet', run: run.id, goal: run.goal, team: run.team, href, text: one.detail, clears: one.clears });
    } else if (one.kind === 'spending') {
      const spent = !run.budget.uncapped && run.budget.cap !== null && run.budget.spent >= run.budget.cap;
      items.push({ reason: spent ? 'spent' : 'budget', run: run.id, goal: run.goal, team: run.team, href, text: one.detail, clears: one.clears });
    }
  }

  return items;
}

/**
 * A node's state, as one of Studio's words. A node that reported itself
 * blocked while its run waits on you is waiting on that answer, not stuck,
 * so it says so rather than showing as a failure.
 */
export function nodeWord(state: string, activity: string, runWaiting = false): string {
  if (activity === 'waiting for you' || (runWaiting && state === 'blocked')) {
    return 'waiting for you';
  }

  switch (state) {
    case 'working':
      return 'working';
    case 'done':
      return 'done';
    case 'blocked':
      return 'blocked';
    case 'failed':
      return 'failed';
    case 'needs-decision':
      return 'needs a decision';
    case 'ended':
      return 'stopped';
    case 'stuck':
      return 'stuck';
    default:
      return activity === 'working' ? 'working' : 'idle';
  }
}

/** The rounds along the lead's line, with the nodes each round's turns belonged to. */
export function shapeOf(run: Run): { rounds: { word: string; lead?: string; nodes?: [string, string][] }[]; next: string | null; end: { word: string; text?: string } | null } {
  const words = new Map(run.nodes.map((node) => [node.node, nodeWord(node.state, node.activity, run.waitingForYou)]));
  const roles = new Map(run.nodes.map((node) => [node.node, node.role]));
  const turns = run.turns ?? [];
  const count = Math.max(run.rounds, ...turns.map((turn) => turn.round), 0);
  const requests = new Map((run.numbers?.rounds ?? []).map((round) => [round.round, round.requests]));
  const rounds = [];

  for (let round = 1; round <= count; round++) {
    const current = run.running && round === count;
    const here = [...new Set(turns.filter((turn) => turn.round === round && roles.get(turn.node) !== 'lead').map((turn) => turn.node))];
    const asked = requests.get(round);

    rounds.push({
      word: current ? (run.waitingForYou ? 'waiting for you' : 'round running') : 'done',
      lead: asked === undefined ? undefined : asked ? `briefed ${asked} ${asked === 1 ? 'node' : 'nodes'}` : 'briefed nobody',
      nodes: here.map((node): [string, string] => [node, current ? (words.get(node) ?? 'idle') : 'done']),
    });
  }

  const end = run.running ? null : { word: runWord(run), text: run.ended ?? undefined };

  // The next round is not drawn: the run does not say what it is for until it briefs.
  return { rounds, next: null, end };
}

/** Who is on the run. */
export function crewOf(run: Run, quietFor: (node: string) => string | null = () => null) {
  return run.nodes.map((node) => ({
    node: node.node,
    role: node.role,
    model: node.model ?? null,
    word: nodeWord(node.state, node.activity, run.waitingForYou),
    doing: node.doing,
    quiet: quietFor(node.node),
    turns: node.turns ?? 0,
    cost: node.cost ?? 0,
    refused: node.denials ?? 0,
  }));
}

/** A run as a row of Team Focus's board: its goal, its rounds as a small line, its state, its spend, and its first question. */
export function boardOf(run: Run, gateFor: (run: Run, gate: RunGate) => GateShown): BoardRun {
  const summary = summaryOf(run);

  return {
    id: run.id,
    goal: run.goal,
    team: run.team,
    project: run.project,
    word: summary.word,
    round: run.rounds,
    rounds: shapeOf(run).rounds.map((round) => ({ word: round.word, nodes: round.nodes })),
    spent: summary.spent,
    cap: summary.cap,
    projected: summary.projected,
    gate: run.gates.length ? gateFor(run, run.gates[0]) : null,
  };
}

/** The teams along the top of the board: waiting for you where any live run of it is, running where any is going. */
export function boardTeamsOf(names: string[], runs: Run[]): BoardTeam[] {
  return names.map((name) => {
    const live = runs.filter((run) => run.team === name && run.running);

    return {
      name,
      word: live.some((run) => run.waitingForYou) ? 'waiting for you' : live.length ? 'running' : 'idle',
      fact: live.length ? `${live.length} live` : 'none live',
    };
  });
}

/** The runs a board shows: every live one, and the newest few that ended, so an old history does not bury what is going. */
export function boardRunsOf(runs: Run[], endedShown = 8): Run[] {
  const ended = runs.filter((run) => !run.running).slice(0, endedShown);

  return [...runs.filter((run) => run.running), ...ended];
}

/** A run's done-when as DoneWhen draws it, from the outcomes the daemon read. */
export function criteriaOf(outcomes: Outcome[]) {
  return outcomes.map((one) => ({
    text: one.criterion,
    verdict: (one.met ? 'met' : one.verdict === 'unmet' || one.verdict === 'not-met' ? 'unmet' : one.verdict === 'not-attempted' ? 'not-attempted' : null) as 'met' | 'unmet' | 'not-attempted' | null,
    project: one.source === 'project',
    meaning: one.reading,
    evidence: one.because ? [one.because] : [],
    delivered: one.delivered.map((item) => `${item.kind} by ${item.node}`),
    cited: one.unfound,
  }));
}

/**
 * The runs a query kept, in the order it kept them: Studio's own live runs,
 * picked out by the ids 'team runs --filter' returned. With no query in force
 * every run is kept, as it came.
 */
export function keptRuns(runs: Run[], kept: { run: string }[] | null): Run[] {
  if (kept === null) {
    return runs;
  }

  const byId = new Map(runs.map((run) => [run.id, run]));

  return kept.map((one) => byId.get(one.run)).filter((run): run is Run => !!run);
}

/**
 * The run a start just made: starting answers before the run has an id, so
 * it is the one of that team and goal that was not listed before. Null until
 * it is.
 */
export function startedRun(runs: Pick<Run, 'id' | 'team' | 'goal'>[], before: string[], team: string, goal: string): string | null {
  return runs.find((run) => !before.includes(run.id) && run.team === team && run.goal.trim() === goal.trim())?.id ?? null;
}
