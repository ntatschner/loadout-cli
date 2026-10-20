// The live terminal's states: what each is called, whether you can type, and
// what the bar under the screen says. Ported from the design system's
// Terminal (bundle.js); the words are the README's, exactly.
import type { Session } from './api';
import type { SignalFamily } from './signals';

export type TerminalState =
  | 'connecting'
  | 'working'
  | 'idle'
  | 'unknown'
  | 'waiting'
  | 'approval'
  | 'background'
  | 'ended'
  | 'error'
  | 'reconnecting'
  | 'watch'
  | 'lost';

export interface TerminalAsk {
  family: SignalFamily;
  /** What the bar says; {code} is the exit code. */
  text: string;
  /** The button beside it, if any. */
  button?: string;
  /** Whether it offers 'loadout resume'. */
  resume?: boolean;
}

export const TERMINAL_STATES: Record<TerminalState, { word: string; typing: boolean; ask?: TerminalAsk }> = {
  connecting: { word: 'connecting', typing: false },
  working: { word: 'working', typing: true },
  idle: { word: 'idle', typing: true },
  unknown: { word: 'unknown', typing: true },
  waiting: { word: 'waiting for you', typing: true, ask: { family: 'needs', text: 'It has finished and is waiting for your next message.', button: 'Type a reply' } },
  approval: {
    word: 'needs approval',
    typing: true,
    ask: { family: 'needs', text: 'It is asking for approval. The choices are in the terminal; answer them there.', button: 'Go to the question' },
  },
  // Quiet on purpose: no ask, so no badge, sound or count. It turns into
  // 'waiting for you' by itself if the work goes silent past its time.
  background: { word: 'waiting on background work', typing: true },
  ended: { word: 'ended', typing: false, ask: { family: 'good', text: 'Ended. Its transcript is kept.', resume: true } },
  error: { word: 'exited with an error', typing: false, ask: { family: 'bad', text: 'Exited with an error (exit code {code}). Its transcript is kept.', resume: true } },
  reconnecting: { word: 'reconnecting', typing: false, ask: { family: 'rest', text: 'Lost touch with the daemon. Reconnecting; the output so far is kept.', button: 'Try now' } },
  watch: { word: 'only watching', typing: false, ask: { family: 'rest', text: 'Only watching: typing is off. Start Studio without --watch-only to type into a session.' } },
  lost: { word: 'lost', typing: false, ask: { family: 'bad', text: 'This session ended when the daemon restarted. Its transcript is kept.', resume: true } },
};

export interface TerminalFacts {
  session: Session | undefined;
  /** Whether the WebSocket is open. */
  connected: boolean;
  /** Whether it closed when it should not have. */
  dropped: boolean;
  /** The exit code the socket reported, undefined before it did. */
  exited: number | null | undefined;
  /** Whether the daemon says the session went with an earlier daemon. */
  lost: boolean;
  watchOnly: boolean;
  /** Minutes since the session last wrote anything, or Infinity before it has. */
  quiet: number;
  idleAfter: number;
}

/**
 * Which state a terminal is in. An ending outranks everything; then a lost
 * connection, then watching; then what the agent's own hooks said; and only
 * then what the terminal alone can tell, which is whether it is writing.
 */
export function terminalState(facts: TerminalFacts): TerminalState {
  const { session } = facts;

  if (facts.lost) {
    return 'lost';
  }

  if (!session) {
    return 'connecting';
  }

  const code = facts.exited !== undefined ? facts.exited : session.ended ? session.exitCode : undefined;

  if (code !== undefined) {
    return code !== null && code !== 0 ? 'error' : 'ended';
  }

  if (facts.dropped) {
    return 'reconnecting';
  }

  if (!facts.connected) {
    return 'connecting';
  }

  if (facts.watchOnly) {
    return 'watch';
  }

  if (session.needs === 'approval') {
    return 'approval';
  }

  if (session.needs === 'waiting') {
    return 'waiting';
  }

  if (session.background) {
    return 'background';
  }

  // Quiet is Infinity before anything has been written.
  if (!Number.isFinite(facts.quiet)) {
    return 'unknown';
  }

  return facts.quiet >= facts.idleAfter ? 'idle' : 'working';
}

/**
 * The command that carries a session on in a terminal: by its conversation's
 * id where the agent gave one. 'resume' looks only in the project of the
 * directory it is typed in, so the project is named, or every project
 * searched where it is not known.
 */
export function resumeCommand(agentSession: string | null | undefined, project?: string | null): string {
  const where = project ? `--project ${project}` : '--all';

  return agentSession ? `$ loadout resume ${agentSession} ${where}` : `$ loadout resume --last ${where}`;
}

/**
 * The largest text size, to a tenth of a pixel within 5–22, at which `fits`
 * says 80 columns go in; 5 where none does. xterm rounds its cells to whole
 * device pixels, so the size is found by asking it rather than worked out
 * from the font: a cell is not a fixed share of an em.
 */
export function largestFitting(fits: (size: number) => boolean, low = 5, high = 22): number {
  let best = low * 10;
  let lo = low * 10;
  let hi = high * 10;

  while (lo <= hi) {
    const middle = Math.floor((lo + hi) / 2);

    if (fits(middle / 10)) {
      best = middle;
      lo = middle + 1;
    } else {
      hi = middle - 1;
    }
  }

  return best / 10;
}
