// A state is a word, a glyph, a colour and a motion: the five families and the
// words Loadout uses for each, ported from the design system's bundle.js.

export type SignalFamily = 'moving' | 'needs' | 'bad' | 'good' | 'rest';

/** Glyphs on a 20x20 grid, drawn with a 2 stroke and round caps. */
export const SIGNAL_GLYPHS: Record<string, string> = {
  play: 'M7 5 L15 10 L7 15 Z',
  round: 'M15.5 7 A6 6 0 1 0 16 11.5 M15.5 3 V7 H11.5',
  needs: 'M10 4 V11.5 M10 15 V15.2',
  failed: 'M6 6 L14 14 M14 6 L6 14',
  blocked: 'M4 10a6 6 0 1 0 12 0a6 6 0 1 0-12 0 M6.5 10 H13.5',
  limited: 'M4 14 A6 6 0 0 1 16 14 M10 14 L6.8 10.6',
  done: 'M5 10.5 L8.5 14 L15 6.5',
  idle: 'M5 10 H15',
  open: 'M5 10a5 5 0 1 0 10 0a5 5 0 1 0-10 0',
  held: 'M7.5 5.5 V14.5 M12.5 5.5 V14.5',
  stopped: 'M6 6 H14 V14 H6 Z',
  unknown: 'M7.5 7.5 A2.5 2.5 0 1 1 10 10 V11.5 M10 14.8 V15',
  dropped: 'M5 15 L15 5',
  shaping: 'M10 3 L16 10 L10 17 L4 10 Z',
};

/** Every state word: its family, glyph and motion. A word not here reads as Rest. */
export const SIGNAL_STATES: Record<string, [SignalFamily, string, string]> = {
  working: ['moving', 'play', 'breathe'],
  running: ['moving', 'play', 'breathe'],
  doing: ['moving', 'play', 'breathe'],
  'round running': ['moving', 'round', 'spin'],
  'needs a decision': ['needs', 'needs', 'pulse'],
  'waiting for you': ['needs', 'needs', 'pulse'],
  failed: ['bad', 'failed', 'hard'],
  'last round failed': ['bad', 'failed', 'hard'],
  blocked: ['bad', 'blocked', 'hard'],
  limited: ['bad', 'limited', 'hard'],
  done: ['good', 'done', 'settle'],
  met: ['good', 'done', 'settle'],
  accepted: ['good', 'done', 'settle'],
  idle: ['rest', 'idle', 'dim'],
  open: ['rest', 'open', 'still'],
  captured: ['rest', 'open', 'still'],
  shaping: ['rest', 'shaping', 'still'],
  held: ['rest', 'held', 'still'],
  'held after this round': ['rest', 'held', 'still'],
  stopped: ['rest', 'stopped', 'still'],
  'not attempted': ['rest', 'open', 'dashed'],
  unknown: ['rest', 'unknown', 'flicker'],
  dropped: ['rest', 'dropped', 'strike'],
  // A session's terminal (Terminal).
  'needs approval': ['needs', 'needs', 'pulse'],
  connecting: ['rest', 'open', 'dashed'],
  reconnecting: ['rest', 'round', 'dashed'],
  ended: ['good', 'done', 'settle'],
  'exited with an error': ['bad', 'failed', 'hard'],
  'only watching': ['rest', 'held', 'still'],
  lost: ['bad', 'blocked', 'hard'],
  // Team runs and their remedies (Teams).
  quiet: ['needs', 'needs', 'pulse'],
  'projected over': ['needs', 'limited', 'pulse'],
  'changed since trusted': ['needs', 'needs', 'pulse'],
  unmet: ['bad', 'failed', 'hard'],
  trusted: ['good', 'done', 'settle'],
  'no verdict': ['rest', 'unknown', 'still'],
  'not trusted': ['rest', 'open', 'still'],
  scheduled: ['rest', 'held', 'still'],
};

export interface SignalOf {
  word: string;
  family: SignalFamily;
  glyph: string;
  motion: string;
}

/** A state word as its family, glyph path and motion. */
export function signalOf(word: string): SignalOf {
  const found = SIGNAL_STATES[word] ?? (['rest', 'open', 'still'] as [SignalFamily, string, string]);

  return { word, family: found[0], glyph: SIGNAL_GLYPHS[found[1]], motion: found[2] };
}

export type Stage = 0 | 1 | 2 | 3 | 4;

export const STAGE_NAMES = ['Captured', 'Shaping', 'Running', 'Needs you', 'Done'] as const;

export interface WorkLike {
  kind?: string;
  stage?: Stage;
  waiting?: boolean;
  /** A session whose agent is asking to be allowed something. */
  approval?: boolean;
  blocked?: boolean | string;
  failed?: boolean | string;
  held?: boolean;
  quiet?: number;
}

/**
 * The state word for a piece of work, as the boards derive it. A quiet
 * session reads idle once it has been quiet for idleAfter minutes.
 */
export function wordFor(item: WorkLike, idleAfter = 10): string {
  if (item.waiting) {
    return item.kind === 'team run' ? 'needs a decision' : item.approval ? 'needs approval' : 'waiting for you';
  }

  if (item.blocked) {
    return 'blocked';
  }

  if (item.failed) {
    return 'last round failed';
  }

  if (item.held) {
    return 'held';
  }

  if (item.stage === 4) {
    return item.kind === 'idea' ? 'accepted' : 'done';
  }

  if (item.stage === 2) {
    if (item.kind === 'session') {
      return (item.quiet ?? 0) >= idleAfter ? 'idle' : 'working';
    }

    return item.kind === 'team run' ? 'running' : item.kind === 'task' ? 'doing' : 'round running';
  }

  if (item.stage === 1) {
    return 'shaping';
  }

  return item.kind === 'task' ? 'open' : 'captured';
}

/** What just happened, as a glyph, by the family of the new state. */
export const MARK_GLYPH: Record<SignalFamily, string> = { needs: '?', bad: '✕', good: '✓', moving: '↻', rest: '·' };

/** Waiting items, most urgent first: a problem before a question, an older question before a newer one. */
export function rankUrgent<T extends { waiting?: boolean; problem?: boolean; askedAt?: number }>(items: T[]): T[] {
  return items
    .filter((item) => item.waiting || item.problem)
    .sort((a, b) => {
      if (!!a.problem !== !!b.problem) {
        return a.problem ? -1 : 1;
      }

      return (a.askedAt ?? 0) - (b.askedAt ?? 0);
    });
}
