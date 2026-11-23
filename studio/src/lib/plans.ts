// An idea's plan, as 'loadout idea plan --json' gives it: before acceptance,
// exactly the document accepting would write; after it, the file it wrote.
// Studio draws the Markdown it is given and renders nothing of its own, so
// what is read here is what the terminal prints.
import { readJson } from './api';

/** What 'idea plan --json' says. Its --json leaves out every property that is null. */
export interface PlanRead {
  id: string;
  project?: string | null;
  state: 'preview' | 'written' | 'missing';
  markdown?: string;
  /** Where the written plan is, relative to the workspace; absent for a preview. */
  path?: string | null;
  /** What 'idea accept --plan-hash' is held to; absent when the file has gone. */
  hash?: string | null;
  /** For a written plan, whether the file was edited since it was accepted; absent when that cannot be told. */
  changed?: boolean | null;
  undecided?: string[];
  gapFilled?: string[];
}

/** The list an idea or its task is on, as the idea commands name it. */
export function listWords(project: string | null | undefined): string[] {
  return project ? ['--project', project] : ['--global'];
}

/** The words for 'idea plan': the idea, its list, and the done-when as accepting would take them. */
export function planWords(id: string, where: string[], doneWhen: string[] = []): string[] {
  return [id, ...where, ...doneWhen];
}

/** Reads the plan. */
export function readPlan(id: string, where: string[], doneWhen: string[] = []): Promise<PlanRead> {
  return readJson<PlanRead>('idea plan', planWords(id, where, doneWhen));
}

/** One line on which plan this is and where it lives. */
export function planSaid(plan: PlanRead): string {
  switch (plan.state) {
    case 'preview':
      return 'A preview: exactly what accepting writes. Nothing is written until you confirm.';
    case 'missing':
      return `This idea was accepted, but its plan is not at ${plan.path ?? 'the place it was written'} any more.`;
    default:
      return plan.changed
        ? `The plan at ${plan.path}, as it is now. It has been edited since it was accepted.`
        : `The plan at ${plan.path}, as acceptance wrote it.`;
  }
}

/** The words 'idea accept' adds to be held to the plan that was read. */
export function holdWords(plan: PlanRead | null): string[] {
  return plan?.state === 'preview' && plan.hash ? [`--plan-hash=${plan.hash}`] : [];
}
