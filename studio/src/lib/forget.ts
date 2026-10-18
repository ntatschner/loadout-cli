// Forgetting a project: the commands that run, in order. Cleanup first,
// while the project is still known, then project remove; every one shown
// before it runs, and a preview adds --dry-run to each.
import { lineFor } from './commandLine';

export type ForgetScope = 'here' | 'everywhere';

export interface ForgetPicks {
  protect: boolean;
  tasks: boolean;
  ideas: boolean;
  topics: boolean;
}

/** What a project has, by id; null while it is still being read. */
export interface ForgetCounts {
  tasks: string[] | null;
  ideas: string[] | null;
  topics: string[] | null;
}

export interface Step {
  path: string;
  words: string[];
  line: string;
}

const step = (path: string, words: string[]): Step => ({ path, words, line: lineFor(path, words) });

export function forgetSteps(slug: string, scope: ForgetScope, picked: ForgetPicks, counts: ForgetCounts, available: boolean, preview = false): Step[] {
  const steps: Step[] = [];

  // The Git protections are in the repository, so only where the repository is.
  if (picked.protect && available) {
    steps.push(step('protect', [slug, '--remove']));
  }

  // Tasks, ideas and topics live in the workspace, which forgetting on this
  // machine only keeps for the other machines.
  if (scope === 'everywhere') {
    if (picked.tasks) {
      steps.push(...(counts.tasks ?? []).map((id) => step('task remove', [id, '--project', slug])));
    }

    if (picked.ideas) {
      steps.push(...(counts.ideas ?? []).map((id) => step('idea remove', [id, '--project', slug])));
    }

    if (picked.topics) {
      steps.push(...(counts.topics ?? []).map((topic) => step('memory delete', [topic, slug, '--yes'])));
    }
  }

  steps.push(step('project remove', scope === 'everywhere' ? [slug, '--from-workspace'] : [slug]));

  return preview ? steps.map((one) => step(one.path, [...one.words, '--dry-run'])) : steps;
}

/** Runs the steps in order, stopping at the first that fails. Says how many ran and which failed. */
export async function runSteps(steps: Step[], run: (one: Step) => Promise<boolean>): Promise<{ ran: number; failed: Step | null }> {
  let ran = 0;

  for (const one of steps) {
    if (!(await run(one))) {
      return { ran, failed: one };
    }

    ran += 1;
  }

  return { ran, failed: null };
}

/** The file count project remove says it left in the workspace, from its own words. */
export function filesLeft(output: string): number | null {
  const found = /(\d+) file\(s\) under/.exec(output);

  return found ? Number(found[1]) : null;
}
