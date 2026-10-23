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
};

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
