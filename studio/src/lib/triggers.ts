// Trigger rules' actions that write, waiting for your yes or no, as the
// Teams page's Needs you shows them. Read with 'trigger approvals' and
// answered with 'trigger approve' and 'trigger reject': nothing here decides
// anything, it only shows what the command says and runs what you press.
import { money, type Act, type NeedItem } from './teams';

/** One approval, as 'trigger approvals --json' lists it. */
export interface TriggerApproval {
  id: string;
  rule: string;
  version?: number | null;
  event: string;
  kind: string;
  subject: string;
  project?: string | null;
  title?: string | null;
  url?: string | null;
  trust?: string | null;
  because?: string | null;
  budgetUsd?: number | null;
  asked: string;
  expires: string;
  approved: boolean;
  then: string;
}

/** The lines each button runs, as the page shows them. */
export const approvalLines = {
  approve: (id: string) => `loadout trigger approve ${id}`,
  reject: (id: string) => `loadout trigger reject ${id}`,
  resume: (rule: string) => `loadout trigger resume ${rule}`,
  release: (rule: string) => `loadout trigger release ${rule}`,
};

/** One rule, as 'trigger list --json' lists it, as far as Needs you reads it. */
export interface TriggerStanding {
  rule: { id: string };
  problem?: string | null;
  paused: boolean;
  headroomUsd?: number | null;
}

/** One stale event, as 'trigger stale --json' lists it. */
export interface TriggerStale {
  rule: string;
  subject: string;
  agesOut: string;
}

/** How the sources are doing, as 'trigger sources --json' says, as far as Needs you reads it. */
export interface TriggerSources {
  github?: { problem?: string | null; credential?: string } | null;
}

/** What the attention buttons do, given by the page. */
export interface AttentionAnswers {
  resume: (rule: string) => void;
  release: (rule: string) => void;
  disabledReason?: string;
}

/**
 * What else trigger rules need of you: a rule paused at a cap, events held
 * past the catch-up window waiting to be released, and a source that has
 * stopped. Each clears itself when the command it offers is run, or the
 * cause goes.
 */
export function attentionNeeds(
  rules: TriggerStanding[],
  stale: TriggerStale[],
  sources: TriggerSources | null,
  answers: AttentionAnswers,
): NeedItem[] {
  const paused: NeedItem[] = rules
    .filter((one) => one.paused)
    .map((one) => ({
      reason: 'paused',
      run: `paused:${one.rule.id}`,
      goal: one.rule.id,
      team: 'trigger rule',
      href: '#teams',
      actions: [{ label: 'Resume', cmd: approvalLines.resume(one.rule.id), disabledReason: answers.disabledReason, onpress: () => answers.resume(one.rule.id) }],
    }));

  const byRule = new Map<string, TriggerStale[]>();
  for (const one of stale) {
    byRule.set(one.rule, [...(byRule.get(one.rule) ?? []), one]);
  }

  const held: NeedItem[] = [...byRule].map(([rule, list]) => {
    const first = [...list].sort((a, b) => a.agesOut.localeCompare(b.agesOut))[0];

    return {
      reason: 'stale',
      run: `stale:${rule}`,
      goal: rule,
      team: 'trigger rule',
      href: '#teams',
      text: `${list.length} event(s) held past the 24-hour window, the first about ${first.subject}. Nothing is dropped before ${new Date(first.agesOut).toLocaleString()}.`,
      actions: [{ label: 'Release', cmd: approvalLines.release(rule), disabledReason: answers.disabledReason, onpress: () => answers.release(rule) }],
    };
  });

  const stopped: NeedItem[] = sources?.github?.problem
    ? [{ reason: 'source', run: 'source:github', goal: 'GitHub', team: 'trigger source', href: '#teams', text: sources.github.problem }]
    : [];

  return [...paused, ...held, ...stopped];
}

/** What an approval's buttons do, given by the page. */
export interface ApprovalAnswers {
  approve: (id: string) => void;
  reject: (id: string) => void;
  disabledReason?: string;
}

/**
 * Each approval as a Needs you item: what it would act on, whose words, what
 * it would spend, and what approving it now would do. An approved one held
 * for headroom can only be rejected, which stops it starting.
 */
export function approvalNeeds(approvals: TriggerApproval[], answers: ApprovalAnswers): NeedItem[] {
  return approvals.map((one) => {
    const outside = one.trust === 'outside' ? ` It comes from outside the collaborators (${one.because ?? 'no association given'}).` : '';
    const about = one.title ? `${one.title} (${one.subject})` : one.subject;
    const text = one.approved
      ? `Approved, and waiting for ${one.rule} to have headroom for ${money(one.budgetUsd)}.`
      : `${one.rule} wants to start a run that writes, on ${one.kind} about ${about}.${outside} It would set aside ${money(one.budgetUsd)}, and ${one.then}.`;

    const actions: Act[] = [
      ...(one.approved
        ? []
        : [{ label: 'Approve', solid: true, kind: 'needs' as const, cmd: approvalLines.approve(one.id), disabledReason: answers.disabledReason, onpress: () => answers.approve(one.id) }]),
      { label: 'Reject', kind: 'danger', cmd: approvalLines.reject(one.id), disabledReason: answers.disabledReason, onpress: () => answers.reject(one.id) },
    ];

    return {
      reason: 'approval',
      run: one.id,
      goal: about,
      team: one.rule,
      href: one.url ?? '#teams',
      text,
      clears: one.approved ? 'it starts, you reject it, or a day passes' : 'you approve or reject it, or a day passes',
      actions,
    };
  });
}
