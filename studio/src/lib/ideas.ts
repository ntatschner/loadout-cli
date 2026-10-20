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

/** The answers as one batch for the idea, in the shape 'idea apply' takes. */
export function answerBatch(id: string, answers: Answer[]): Record<string, string>[] {
  return answers.map((one) => ({ op: 'answer', id, question: one.question, answer: one.answer }));
}

/**
 * Sends the answers together, then a refine when asked for. They are made
 * together or not at all, so a refine never runs on half of them, and one
 * refused leaves none of them recorded rather than the ones before it.
 */
export async function sendAnswers(
  id: string,
  where: string[],
  answers: Answer[],
  refine: boolean,
  answerAll: (operations: Record<string, string>[]) => Promise<boolean>,
  act: (path: string, words: string[]) => Promise<boolean>,
): Promise<boolean> {
  if (answers.length > 0 && !(await answerAll(answerBatch(id, answers)))) {
    return false;
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

/** A question's chips: its options, with the recommendation marked or added, and any reason left over. */
export interface Offered {
  choices: string[];
  recommended: number | null;
  note: string | null;
}

const collapse = (text: string) => text.trim().replace(/\s+/g, ' ');

/** True when the shorter of the two starts the longer and ends at a word. */
function startsAtWord(longer: string, shorter: string): boolean {
  return longer.startsWith(shorter) && (longer.length === shorter.length || !/[\p{L}\p{N}]/u.test(longer[shorter.length]));
}

/**
 * Which option the agent recommends, so it can be picked rather than retyped.
 * An exact match ignoring case and spaces first, then the longest option that
 * starts the recommendation or that the recommendation starts, ending at a
 * word. A tie would be a guess, so it counts as no match, and a recommendation
 * that matches nothing becomes a chip of its own. IdeaWork.Recommend applies
 * the same rule as rounds come in; both are tested against
 * tests/Loadout.Tests/Fixtures/recommendations.json.
 */
export function offeredChoices(question: Pick<Question, 'options' | 'recommendation'>): Offered {
  const choices = [...question.options];
  const wanted = collapse(question.recommendation ?? '');

  if (!wanted) {
    return { choices, recommended: null, note: null };
  }

  const said = wanted.toLowerCase();
  const exact = choices.findIndex((one) => collapse(one).toLowerCase() === said);

  if (exact >= 0) {
    return { choices, recommended: exact, note: null };
  }

  let best = -1;
  let length = 0;
  let tied = false;

  choices.forEach((one, index) => {
    const option = collapse(one).toLowerCase();
    const shared = startsAtWord(said, option) ? option.length : startsAtWord(option, said) ? said.length : 0;

    if (shared > length) {
      [best, length, tied] = [index, shared, false];
    } else if (shared > 0 && shared === length) {
      tied = true;
    }
  });

  if (best < 0 || tied) {
    return { choices: [...choices, wanted], recommended: choices.length, note: null };
  }

  const rest = wanted.slice(collapse(choices[best]).length).replace(/^[\s,;:.\-–—]+/, '');

  return { choices, recommended: best, note: rest || null };
}

/**
 * Drafts with every recommendation taken, for the questions still open with
 * nothing drafted. Drafts already there are kept, and nothing is sent.
 */
export function acceptRecommendations(questions: Question[], drafts: Record<string, string>, open: (question: Question) => boolean): Record<string, string> {
  const next = { ...drafts };

  for (const one of questions) {
    const { choices, recommended } = offeredChoices(one);

    if (recommended !== null && open(one) && !(next[one.id] ?? '').trim()) {
      next[one.id] = choices[recommended];
    }
  }

  return next;
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

/** A piece of a plan as Next sees it: a layer with its options, or an addition with none. */
export interface Piece {
  id: string;
  verdict: string;
  chosen?: string | null;
  options?: unknown[];
}

/** What is typed on the page and not sent: the questions with an answer drafted, those sent and not yet read back, and the pieces with a rework request written. */
export interface Drafts {
  answers: string[];
  sent: string[];
  reworks: string[];
}

export const NO_DRAFTS: Drafts = { answers: [], sent: [], reworks: [] };

/** Where Next goes: one question, the send button, a rework's Send it back, a layer's options or a piece's verdict, or Accept. */
export type Stop =
  | { kind: 'question'; id: string; index: number; of: number }
  | { kind: 'send-answers' }
  | { kind: 'rework'; id: string }
  | { kind: 'choose'; id: string }
  | { kind: 'verdict'; id: string }
  | { kind: 'accept' };

const said = (verdict: string) => String(verdict || 'undecided').toLowerCase();

/** A layer with options to choose from, none chosen, and not dropped: what the Accept warning counts. */
export function needsChoice(piece: Piece): boolean {
  return !!piece.options?.length && !piece.chosen && said(piece.verdict) !== 'drop';
}

/** A piece nobody has kept, dropped or sent back yet. */
export function needsVerdict(piece: Piece): boolean {
  return said(piece.verdict) === 'undecided';
}

interface Shaped {
  questions: Question[];
  plan: { layers: Piece[]; additions: Piece[] } | null;
  accepted: unknown;
}

/** The questions still open: no answer saved, none sent, and nothing typed. */
function openQuestions(idea: Shaped, drafts: Drafts): Question[] {
  return idea.questions.filter((one) => !one.answer && !drafts.sent.includes(one.id) && !drafts.answers.includes(one.id));
}

/** Every gap on the idea, in page order: open questions, then each layer's choice and verdict, then each addition's verdict. Unsent drafts are not gaps. */
export function gaps(idea: Shaped, drafts: Drafts = NO_DRAFTS): Stop[] {
  if (idea.accepted) {
    return [];
  }

  const found: Stop[] = openQuestions(idea, drafts).map((one) => ({ kind: 'question', id: one.id, index: idea.questions.indexOf(one) + 1, of: idea.questions.length }));

  for (const piece of [...(idea.plan?.layers ?? []), ...(idea.plan?.additions ?? [])]) {
    if (needsChoice(piece)) {
      found.push({ kind: 'choose', id: piece.id });
    }

    if (needsVerdict(piece)) {
      found.push({ kind: 'verdict', id: piece.id });
    }
  }

  return found;
}

/**
 * Where Next goes from here. Open questions come first; once every one has
 * something typed, the answers are sent; then a half-written rework; then
 * the first gap in the plan; and with nothing left, Accept. Null when there
 * is nowhere to go: accepted already, or nothing yet to accept.
 */
export function nextStop(idea: Shaped, drafts: Drafts = NO_DRAFTS): Stop | null {
  if (idea.accepted) {
    return null;
  }

  const found = gaps(idea, drafts);
  const question = found.find((one) => one.kind === 'question');

  if (question) {
    return question;
  }

  if (drafts.answers.length) {
    return { kind: 'send-answers' };
  }

  const pieces = [...(idea.plan?.layers ?? []), ...(idea.plan?.additions ?? [])];
  const rework = pieces.find((one) => drafts.reworks.includes(one.id));

  if (rework) {
    return { kind: 'rework', id: rework.id };
  }

  return found[0] ?? (idea.plan ? { kind: 'accept' } : null);
}

/** What the Next button says about where it goes. */
export function stopWords(stop: Stop): string {
  switch (stop.kind) {
    case 'question':
      return `answer ${stop.index} of ${stop.of}`;
    case 'send-answers':
      return 'send answers';
    case 'rework':
      return `send ${stop.id} back`;
    case 'choose':
      return `choose for ${stop.id}`;
    case 'verdict':
      return `keep or drop ${stop.id}`;
    case 'accept':
      return 'accept the plan';
  }
}

/** The data-next value the stop's target carries on the page. */
export function stopTarget(stop: Stop): string {
  return 'id' in stop ? `${stop.kind}:${stop.id}` : stop.kind;
}

/**
 * The fresh read of an idea, with every part that did not change kept as the
 * object already on the page, so what draws it is left alone. Lists of
 * things with ids are matched by id, so a reordered or lengthened list keeps
 * the parts it still holds.
 */
export function keepUnchanged<T>(was: T, now: T): T {
  if (was === now || JSON.stringify(was) === JSON.stringify(now)) {
    return was;
  }

  if (Array.isArray(was) && Array.isArray(now)) {
    const byId = new Map(was.filter((one) => one && typeof one === 'object' && 'id' in one).map((one) => [(one as { id: unknown }).id, one]));

    return now.map((one, index) => {
      const same = one && typeof one === 'object' && 'id' in one ? byId.get((one as { id: unknown }).id) : was[index];

      return same === undefined ? one : keepUnchanged(same, one);
    }) as T;
  }

  if (was && now && typeof was === 'object' && typeof now === 'object' && !Array.isArray(was) && !Array.isArray(now)) {
    const kept: Record<string, unknown> = {};

    for (const [key, value] of Object.entries(now)) {
      kept[key] = key in was ? keepUnchanged((was as Record<string, unknown>)[key], value) : value;
    }

    return kept as T;
  }

  return now;
}

/** Something a round brought that the page takes you to: questions not asked before, or a later revision of the plan. */
export type Arrival = 'questions' | 'revision';

/**
 * What a fresh read of the same idea brought that is new. A choice, an
 * answer or a verdict being recorded is not: only questions with ids that
 * were not there before, or a plan whose revision went up. Questions come
 * first, as they do on the page.
 */
export function arrivalIn(was: { questions: { id: string }[]; plan: { revision: number } | null }, now: { questions: { id: string }[]; plan: { revision: number } | null }): Arrival | null {
  const asked = new Set(was.questions.map((one) => one.id));

  if (now.questions.some((one) => !asked.has(one.id))) {
    return 'questions';
  }

  if (now.plan && now.plan.revision > (was.plan?.revision ?? 0)) {
    return 'revision';
  }

  return null;
}
