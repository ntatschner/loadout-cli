// Bringing open ideas and tasks up to the current contract, as Studio shows
// it: the marks on a list's rows, and what 'loadout backfill contract
// --dry-run' found, in the words the dialog uses before anything is asked.

/** The mark a row carries, as 'task list' and 'idea list' give it. */
export type ContractMark = 'missing' | 'proposalWaiting';

/** The words a row shows for its mark, or null for none. */
export function markWord(mark: string | null | undefined): string | null {
  return mark === 'missing' ? 'missing' : mark === 'proposalWaiting' ? 'proposal waiting' : null;
}

/** One record a pass looks at, as '--json' gives it. */
export interface BackfillFound {
  kind: 'idea' | 'task';
  project?: string | null;
  id: string;
  title: string;
  missing: string[];
  /** Whether a real pass asks an agent about it: false when skipped, or past --limit. */
  asks: boolean;
  skipped?: string | null;
  lastFailure?: string | null;
}

/** What a pass found, and what it did. */
export interface BackfillOutcome {
  lists: string[];
  dryRun: boolean;
  calls: number;
  found: BackfillFound[];
  results: { kind: string; project?: string | null; id: string; filled: boolean; doneWhen: string[]; error?: string | null; costUsd: number }[];
}

/** The words every pass from Studio runs with: every list, since a page has no project it is in. */
export const BACKFILL_WORDS = ['--all-projects'];

/** A contract field as a person reads it: 'doneWhen' is a done-when. */
export const fieldWord = (field: string) => (field === 'doneWhen' ? 'done-when' : field);

const plural = (count: number, one: string) => `${count} ${one}${count === 1 ? '' : 's'}`;

/** The dialog's opening line. */
export function backfillSummary(outcome: BackfillOutcome): string {
  if (outcome.found.length === 0) {
    return 'Nothing on any list is short of the current contract.';
  }

  const skipped = outcome.found.filter((one) => one.skipped).length;

  return (
    `${plural(outcome.found.length, 'record')} short of the current contract. ` +
    `A pass would ask an agent ${plural(outcome.calls, 'time')}` +
    (skipped ? `, and leave ${skipped} alone, each with its reason.` : '.')
  );
}

/** What the dialog says about one record. */
export function backfillLine(one: BackfillFound): string {
  const where = `${one.project ?? 'workspace-wide'}/${one.id}`;
  const lacks = `lacks a ${one.missing.map(fieldWord).join(', ')}`;

  if (one.skipped) {
    return `${one.kind} ${where}, ${lacks}. Skipped: ${one.skipped}`;
  }

  return `${one.kind} ${where}, ${lacks}.` + (one.asks ? '' : ' Left for a later pass.') + (one.lastFailure ? ` Last attempt failed: ${one.lastFailure}` : '');
}

/** How long a pass is waited for before it is left to finish on its own: a few minutes a call. */
export const backfillPatience = (calls: number) => Math.max(120_000, calls * 240_000);
