// The Ideas page's rules, apart from the page: which answers go, the words
// each answer is sent with, the order they go in, and which pieces of a dump
// are recorded. Every line is built with lineFor, so the page shows what runs.
import { lineFor } from './commandLine';

export interface Question {
  id: string;
  question: string;
  why: string | null;
  options: string[];
  recommendation: string | null;
  answer: string | null;
}

/** One answer ready to send: the question it answers, the text, and its exact words and line. */
export interface Answer {
  question: string;
  answer: string;
  words: string[];
  line: string;
}

export const STAGES = ['Captured', 'Answering', 'Ready', 'Proposed', 'Accepted'] as const;

/** Where an idea is, as the stage index idea list's stage word gives. */
export function stageIndex(stage: string): number {
  return Math.max(0, STAGES.findIndex((one) => one.toLowerCase() === String(stage).toLowerCase()));
}

/** What the shelf and the stages say about an idea: a round running, a failed one, questions waiting, accepted, or being shaped. */
export function stateWord(one: { running?: boolean; failure?: unknown; unanswered?: number; stage: string }): string {
  if (one.running) {
    return 'round running';
  }

  if (one.failure) {
    return 'last round failed';
  }

  if ((one.unanswered ?? 0) > 0) {
    return 'waiting for you';
  }

  return String(one.stage).toLowerCase() === 'accepted' ? 'accepted' : 'shaping';
}

/**
 * The words for one answer. The answer goes joined to its option with '=',
 * because the parser refuses a value that starts with a dash given any
 * other way.
 */
export function answerWords(id: string, question: string, answer: string, where: string[]): string[] {
  return [id, question, `--answer=${answer}`, ...where];
}

/** Only the answers that changed and say something, each with the line it runs. */
export function changedAnswers(id: string, where: string[], questions: Question[], drafts: Record<string, string>): Answer[] {
  return questions
    .filter((one) => {
      const draft = (drafts[one.id] ?? '').trim();

      return draft.length > 0 && draft !== (one.answer ?? '');
    })
    .map((one) => {
      const answer = drafts[one.id].trim();
      const words = answerWords(id, one.id, answer, where);

      return { question: one.id, answer, words, line: lineFor('idea answer', words) };
    });
}

/**
 * Sends the answers one at a time, then a refine when asked for. It stops at
 * the first that fails, so a refine never runs on half the answers.
 */
export async function sendAnswers(
  id: string,
  where: string[],
  answers: Answer[],
  refine: boolean,
  act: (path: string, words: string[]) => Promise<boolean>,
): Promise<boolean> {
  for (const one of answers) {
    if (!(await act('idea answer', one.words))) {
      return false;
    }
  }

  return refine ? act('idea refine', [id, ...where]) : true;
}

/** Drafts that are now the saved answer are dropped: the page shows them as answered. */
export function settledDrafts(questions: Question[], drafts: Record<string, string>): Record<string, string> {
  const kept: Record<string, string> = {};

  for (const [question, draft] of Object.entries(drafts)) {
    if (questions.find((one) => one.id === question)?.answer !== draft.trim()) {
      kept[question] = draft;
    }
  }

  return kept;
}

export interface DumpItem {
  number: number;
  title: string;
  excerpt?: string;
  kind: string;
  recorded?: string | boolean | null;
}

/** The words for recording a dump: --only when some unrecorded pieces are left unticked. */
export function applyWords(id: string, items: DumpItem[], picked: number[]): string[] {
  const open = items.filter((one) => !one.recorded).length;
  const chosen = [...picked].sort((a, b) => a - b);

  return chosen.length && chosen.length < open ? [id, '--only', chosen.join(',')] : [id];
}

/** Where an accepted plan goes: onto a project, or as a new one. */
export function acceptWords(id: string, where: string[], to: { project: string } | { newProject: string }): string[] {
  return 'project' in to ? [id, ...where, '--to', to.project] : [id, ...where, `--new-project=${to.newProject}`];
}
