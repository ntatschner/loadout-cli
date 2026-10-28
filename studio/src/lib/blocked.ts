// Runs that ended short of their done-when, as 'loadout team blocked list'
// reads them, and the words 'team blocked resolve' is given for each choice.
// Kept apart from the component so the words a button sends are tested
// without a page: the command line shown beside a button has to be the one
// it runs.

export type BlockedAction = 'carry-on' | 'raise' | 'requeue' | 'edit-requeue' | 'set-aside' | 'drop';

export interface BlockedRead {
  id: string;
  team: string;
  project: string | null;
  task: string | null;
  goal: string;
  kind: 'blocked' | 'failed' | 'limited' | 'unrecorded' | 'dead' | 'handover' | 'stalled' | 'unfinished';
  ended: string | null;
  cost: number;
  budget: number | null;
  rounds: number;
  roundLimit: number;
  origin: string | null;
  follows: string | null;
  chain: number;
  merged: string[];
  unmerged: string[];
  lead: { blocker: string | null; unblockedBy: string | null; questions: string[]; next: string | null; summary: string | null } | null;
  suggested: { action: BlockedAction; label: string; reason: string; rule: string };
  offered: { action: BlockedAction; label: string }[];
  raise: { cap: 'rounds' | 'budget'; figure: number; used: number; said: string } | null;
}

/** What a person filled in before confirming. */
export interface BlockedChoice {
  action: BlockedAction;
  answer: string;
  /** For a raise: the rounds or dollars, as typed. */
  figure: string;
  title: string;
  detail: string;
  /** For edit-requeue: one criterion per line. */
  doneWhen: string;
}

/** A choice to start from: the suggestion, with a raise's figure filled in as the same again. */
export function choiceFor(run: BlockedRead): BlockedChoice {
  return {
    action: run.suggested.action,
    answer: '',
    figure: run.raise ? String(run.raise.cap === 'budget' ? run.raise.figure.toFixed(2) : run.raise.figure) : '',
    title: '',
    detail: '',
    doneWhen: '',
  };
}

/** Why the choice cannot be confirmed yet, or null when it can. */
export function notReady(run: BlockedRead, choice: BlockedChoice): string | null {
  if (!run.offered.some((one) => one.action === choice.action)) {
    return 'that is not open to this run';
  }

  if (choice.action === 'raise') {
    const figure = Number(choice.figure.trim().replace(/^\$/, ''));

    if (!choice.figure.trim() || !Number.isFinite(figure) || figure <= 0) {
      return run.raise?.cap === 'rounds' ? 'say how many rounds' : 'say how much it may spend';
    }

    if (run.raise?.cap === 'rounds' && !Number.isInteger(figure)) {
      return 'rounds are a whole number';
    }
  }

  if (choice.action === 'edit-requeue' && !choice.title.trim() && !choice.detail.trim() && !lines(choice.doneWhen).length) {
    return 'say what changes: the title, the detail or the done-when';
  }

  return null;
}

/** The words after 'team blocked resolve'. Free text is joined with '=', since the parser refuses a value starting with a dash. */
export function resolveWords(run: BlockedRead, choice: BlockedChoice): string[] {
  const words = [run.id, '--action', choice.action];

  if (choice.answer.trim()) {
    words.push(`--answer=${choice.answer.trim()}`);
  }

  if (choice.action === 'raise' && run.raise) {
    const figure = choice.figure.trim().replace(/^\$/, '');

    words.push(run.raise.cap === 'rounds' ? `--rounds=${figure}` : `--usd=${figure}`);
  }

  if (choice.action === 'edit-requeue') {
    if (choice.title.trim()) {
      words.push(`--title=${choice.title.trim()}`);
    }

    if (choice.detail.trim()) {
      words.push(`--detail=${choice.detail.trim()}`);
    }

    for (const criterion of lines(choice.doneWhen)) {
      words.push(`--done-when=${criterion}`);
    }
  }

  return words;
}

/** The line shown beside the confirm button. */
export function resolveLine(run: BlockedRead, choice: BlockedChoice): string {
  return `$ loadout team blocked resolve ${resolveWords(run, choice)
    .map((word) => (/\s/.test(word) ? `"${word.replace(/"/g, '\\"')}"` : word))
    .join(' ')}`;
}

/** How far the run got, in a line, for beside a raise or a carry-on. */
export function gotTo(run: BlockedRead): string {
  const money = `$${run.cost.toFixed(2)}${run.budget != null ? ` of $${run.budget.toFixed(2)}` : ''}`;
  const branches = run.merged.length || run.unmerged.length ? ` · ${run.merged.length} merged, ${run.unmerged.length} left on branches` : '';

  return `${run.rounds} of ${run.roundLimit} round(s) · ${money}${branches}`;
}

function lines(text: string): string[] {
  return text
    .split(/\r?\n/)
    .map((one) => one.trim())
    .filter(Boolean);
}
