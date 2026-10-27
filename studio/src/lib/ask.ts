// Ask Loadout from the palette: '?' and a request in plain words, answered by
// 'loadout ask' through the daemon. Anything that would start something comes
// back as a plan, carried out only when somebody approves it here.
import { readJson, type Job } from './api';
import { runJob } from './jobs';
import type { AskConversation } from './askConversation.svelte';

export type AskShape = 'spend' | 'tasks' | 'team-queue' | 'terminals';

export interface AskStep {
  kind: 'queue-task' | 'start-team' | 'open-terminal';
  project: string | null;
  task: string | null;
  says: string;
  reasons: string[];
}

export interface AskPlan {
  shape: AskShape;
  team: string | null;
  teamChoices: string[];
  steps: AskStep[];
  capped: boolean;
  /** The cap it starts with, from team-budget, or null. */
  cap: number | null;
  /** team-budget says 'none': no cap, on purpose. */
  capNone: boolean;
  left: string[];
  needs: string[];
}

export interface AskSpend {
  estimate: true;
  basis: string;
  pricesCheckedOn: string;
  usd: number;
  unpricedTokens: number;
  unpricedModels: string[];
  models: { model: string; tokens: number; usd: number | null }[];
  caveat: string | null;
}

export interface AskTask {
  project: string;
  id: string;
  title: string;
  score: number;
  reasons: string[];
}

export interface AskReply {
  query: string;
  kind: 'answer' | 'plan' | 'unplaced';
  answeredBy: 'loadout' | 'agent';
  summary: string;
  spend: AskSpend | null;
  tasks: AskTask[];
  plan: AskPlan | null;
  nearMisses: { shape: AskShape; rewrite: string }[];
  /** What asking the agent instead would cost, roughly, when nothing was placed. */
  agentOffer: { estimatedUsd: number; basis: string } | null;
  /** What the agent's turn cost, when it answered. Never part of a plan's cap. */
  agentCostUsd: number | null;
  /** The agent's conversation, for asking it the next request in the same one. */
  agentSession: string | null;
}

/** One request and what came back, or why nothing did. */
export interface AskTurn {
  id: number;
  query: string;
  reply: AskReply | null;
  failed: string | null;
}

/** Whether what is typed in the palette is a request for Loadout rather than a search. */
export function isAsk(typed: string): boolean {
  return typed.trimStart().startsWith('?');
}

/** The request without the '?' that asks it. */
export function request(typed: string): string {
  return typed.trimStart().replace(/^\?+/, '').trim();
}

/** Asks Loadout. The request goes as --query= so one starting with a minus is not read as an option. */
export async function askLoadout(query: string): Promise<AskReply> {
  return arrived(await readJson<unknown>('ask', [`--query=${query}`]));
}

/** How long an agent turn may take before the page stops waiting for it. */
export const AGENT_PATIENCE = 300_000;

/**
 * Asks the agent, because somebody chose to. A job rather than a read: it
 * spends a turn, so a watch-only Studio refuses it.
 */
export async function askAgent(
  query: string,
  resume: string | null,
  run: (path: string, words: string[], patience: number) => Promise<Job> = runJob,
): Promise<AskReply> {
  const job = await run('ask', ['--use-agent', `--query=${query}`, ...(resume ? ['--resume', resume] : []), '--json'], AGENT_PATIENCE);

  return arrived(JSON.parse(job.output ?? 'null'));
}

/**
 * A reply as the command wrote it, with what it left out filled in. A
 * command's --json leaves out every property that is null, so a plan with
 * no cap arrives with no cap at all, and a page reading it would show
 * 'undefined' in the cap box.
 */
export function arrived(wire: unknown): AskReply {
  const reply = wire as Partial<AskReply> & { query: string; kind: AskReply['kind']; summary: string };
  const plan = reply.plan as Partial<AskPlan> | undefined;
  const spend = reply.spend as Partial<AskSpend> | undefined;

  return {
    query: reply.query,
    kind: reply.kind,
    answeredBy: reply.answeredBy ?? 'loadout',
    summary: reply.summary,
    tasks: reply.tasks ?? [],
    nearMisses: reply.nearMisses ?? [],
    agentOffer: reply.agentOffer ?? null,
    agentCostUsd: reply.agentCostUsd ?? null,
    agentSession: reply.agentSession ?? null,
    spend: spend
      ? {
          estimate: true,
          basis: spend.basis ?? 'list rates',
          pricesCheckedOn: spend.pricesCheckedOn ?? '',
          usd: spend.usd ?? 0,
          unpricedTokens: spend.unpricedTokens ?? 0,
          unpricedModels: spend.unpricedModels ?? [],
          models: (spend.models ?? []).map((model) => ({ model: model.model, tokens: model.tokens ?? 0, usd: model.usd ?? null })),
          caveat: spend.caveat ?? null,
        }
      : null,
    plan: plan
      ? {
          shape: plan.shape!,
          team: plan.team ?? null,
          teamChoices: plan.teamChoices ?? [],
          steps: (plan.steps ?? []).map((step) => ({ ...step, project: step.project ?? null, task: step.task ?? null, reasons: step.reasons ?? [] })),
          capped: plan.capped ?? false,
          cap: plan.cap ?? null,
          capNone: plan.capNone ?? false,
          left: plan.left ?? [],
          needs: plan.needs ?? [],
        }
      : null,
  };
}

/**
 * Why a plan can't be approved yet, or null when it can. The cap is required
 * whenever the plan carries one and team-budget gave none.
 */
export function approveReason(plan: AskPlan, team: string | null, cap: string): string | null {
  if (!plan.steps.some((step) => step.task)) {
    return 'No task qualified, so there is nothing to start.';
  }

  if (plan.shape === 'team-queue' && !team) {
    return 'Choose a team first.';
  }

  if (plan.capped) {
    const typed = cap.trim().replace(/^\$/, '');

    if (!typed) {
      return 'Give a spending cap first. team-budget is not set, so there is no default.';
    }

    if (typed.toLowerCase() !== 'none' && !(Number(typed) > 0)) {
      return 'The cap is a figure in US dollars above zero, or none.';
    }
  }

  return null;
}

/** The cap the card starts with: team-budget's figure, 'none' when it says so, or blank. */
export function startingCap(plan: AskPlan): string {
  return plan.cap !== null ? String(plan.cap) : plan.capNone ? 'none' : '';
}

/** The words of 'loadout team queue run-now' for an approved team plan. */
export function runNowWords(plan: AskPlan, team: string, cap: string): string[] {
  const tasks = plan.steps
    .filter((step) => step.kind === 'queue-task' && step.task && step.project)
    .flatMap((step) => ['--task', `${step.project}/${step.task}`]);

  return [team, ...tasks, '--usd', cap.trim().replace(/^\$/, '')];
}

/** A dollar figure as an answer shows it. */
export function usd(amount: number): string {
  return `$${amount.toLocaleString('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

/** What the palette needs to ask Loadout and carry out a plan. */
export interface PaletteAsk {
  conversation: AskConversation;
  asker: (query: string) => Promise<AskReply>;
  /** Asks the agent, carrying on its conversation when there is one. */
  agent: (query: string, resume: string | null) => Promise<AskReply>;
  approve: (plan: AskPlan, team: string | null, cap: string) => Promise<string>;
}

/** What carrying out a plan needs from Studio, passed in so it can be tested without a daemon. */
export interface PlanRunner {
  /** Starts a command as a job and returns without waiting for it to end. */
  start: (path: string, words: string[]) => Promise<unknown>;
  /** Runs a command and waits for it, throwing with its own words when it fails. */
  run: (path: string, words: string[]) => Promise<unknown>;
  /** Opens a terminal Studio hosts, with a task loaded. */
  openTerminal: (project: string, task: string) => Promise<unknown>;
}

/**
 * Carries out an approved plan and says what happened. A team plan that
 * starts runs as one job, 'team queue run-now', which takes the cap; one that
 * only queues adds each task in turn; terminals open one by one.
 */
export async function approvePlan(plan: AskPlan, team: string | null, cap: string, runner: PlanRunner): Promise<string> {
  const reason = approveReason(plan, team, cap);

  if (reason) {
    throw new Error(reason);
  }

  const tasks = plan.steps.filter((step) => step.task && step.project);

  if (plan.shape === 'terminals') {
    for (const step of tasks) {
      await runner.openTerminal(step.project!, step.task!);
    }

    return `Opened ${tasks.length} terminal(s), each with its task. They are on the Sessions page.`;
  }

  if (plan.steps.some((step) => step.kind === 'start-team')) {
    await runner.start('team queue run-now', runNowWords(plan, team!, cap));

    return `Started ${team} on ${tasks.length} task(s), sharing a cap of ${cap.trim().toLowerCase() === 'none' ? 'none' : usd(Number(cap.trim().replace(/^\$/, '')))}. The Jobs strip shows how it goes.`;
  }

  for (const step of tasks) {
    await runner.run('team queue add', [team!, step.task!, '--project', step.project!]);
  }

  return `Queued ${tasks.length} task(s) for ${team}. Nothing has started.`;
}
