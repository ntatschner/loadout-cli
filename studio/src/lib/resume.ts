// Resuming a past conversation from Studio. The daemon decides what is
// resumed and what carries over, with the same service 'loadout resume'
// uses; what is here is the asking. A conversation already in one of the
// daemon's terminals is gone to, not started twice; one that looks to be
// running somewhere else, or ran in a folder that is not a project, is only
// resumed once the person has said so.
import type { Conversation, Launch, LostSession, Resume, Resumed, Session } from './api';

/** A question the page asks before trying again. */
export type ResumeAsk =
  | { kind: 'elsewhere'; conversation: Conversation }
  | { kind: 'unregistered'; conversation: Conversation; directory: string };

export interface ResumeDeps {
  resume: (resume: Resume, sheet?: Partial<Launch>) => Promise<Resumed>;
  /** Resolves true when the person says go ahead. */
  ask: (question: ResumeAsk) => Promise<boolean>;
}

export type ResumeOutcome = { started: Session } | { goTo: string } | { declined: true };

/** The daemon's terminal already running a conversation, or null. */
export function hostedIn(running: string): string | null {
  return running.startsWith('hosted:') ? running.slice('hosted:'.length) || null : null;
}

/**
 * Resumes a conversation, asking what has to be asked. At most one question
 * of each kind: the daemon answers a confirmed resume with a start or a
 * refusal, never the same question again.
 */
export async function resumeConversation(
  conversation: Conversation,
  deps: ResumeDeps,
  sheet: Partial<Launch> = {},
  lost?: string,
  fresh = false,
): Promise<ResumeOutcome> {
  const there = hostedIn(conversation.running);

  if (there) {
    return { goTo: there };
  }

  const asking: Resume = { agentSession: conversation.id, agent: conversation.agent, lost, fresh: fresh || undefined };

  for (let attempt = 0; attempt < 3; attempt++) {
    const answer = await deps.resume(asking, sheet);

    if ('started' in answer) {
      return answer;
    }

    switch (answer.conflict) {
      case 'hosted':
        return { goTo: answer.session };

      case 'elsewhere':
        if (asking.confirmRunning || !(await deps.ask({ kind: 'elsewhere', conversation }))) {
          return { declined: true };
        }

        asking.confirmRunning = true;
        break;

      case 'unregistered':
        if (asking.registerFolder || !(await deps.ask({ kind: 'unregistered', conversation, directory: answer.directory }))) {
          return { declined: true };
        }

        asking.registerFolder = answer.directory;
        break;
    }
  }

  return { declined: true };
}

/** What carries over into a resume, as somebody would say it. */
export function carriedWords(conversation: Conversation): string {
  const launched = conversation.launchedFor;

  if (!launched) {
    return 'nothing carried over: no launch on record';
  }

  const parts = [
    launched.task ? `task ‘${launched.task}’` : null,
    launched.mode ? `mode ${launched.mode}` : null,
    launched.profile ? `profile ${launched.profile}` : null,
    launched.worktree ? `worktree ${launched.worktree}` : null,
  ].filter((part): part is string => part !== null);

  return parts.length ? `carries over ${parts.join(', ')}` : 'launched with nothing to carry over';
}

/**
 * What 'Resume all' says before it starts anything: how many, and with which
 * agents. Each one starts an agent, which costs money, so the count is said
 * rather than implied.
 */
export function resumeAllWords(lost: LostSession[]): string {
  const resumable = lost.filter((one) => one.conversation);
  const byAgent = new Map<string, number>();

  for (const one of resumable) {
    byAgent.set(one.conversation!.agent, (byAgent.get(one.conversation!.agent) ?? 0) + 1);
  }

  const agents = [...byAgent].map(([agent, count]) => `${count} with ${agent}`).join(', ');
  const count = resumable.length;

  return `This starts ${count} ${count === 1 ? 'agent' : 'agents'}: ${agents}. Each one carries on its conversation and costs what a session costs.`;
}

/** The terminal command a resume runs, for the page to show beneath the button. */
export function resumeWords(conversation: Conversation, sheet: Partial<Launch> = {}, fresh = false): string {
  const words = ['$ loadout resume', conversation.id];

  if (conversation.project) {
    words.push(`--project ${conversation.project}`);
  }

  if (fresh) {
    words.push('--fresh');
  }

  if (sheet.taskId) {
    words.push(`--task-id ${sheet.taskId}`);
  }

  if (sheet.task) {
    words.push(`--task "${sheet.task}"`);
  }

  if (sheet.mode) {
    words.push(`--mode ${sheet.mode}`);
  }

  if (sheet.profile) {
    words.push(`--profile ${sheet.profile}`);
  }

  if (sheet.worktree) {
    words.push(`--worktree ${sheet.worktree}`);
  }

  if (sheet.offline) {
    words.push('--offline');
  }

  if (sheet.noSync) {
    words.push('--no-sync');
  }

  return words.join(' ');
}
