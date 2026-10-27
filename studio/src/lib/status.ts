// What a hosted session's status line last handed over, as the daemon serves
// it on the session row, and the few readings every place that draws it
// shares: how full the context is, whether the snapshot is old, and how the
// figures are written. One shape drives the strip, the Details block and the
// marks on tabs and in Pure, so they cannot disagree.

/** Claude's context window, as it counted it. */
export interface StatusContext {
  usedPercent: number | null;
  inputTokens: number | null;
  windowSize: number | null;
  uncachedTokens: number | null;
  cacheReadTokens: number | null;
  cacheWriteTokens: number | null;
  outputTokens: number | null;
}

export interface StatusWindow {
  usedPercent: number;
  resetsAt: string | null;
}

export interface StatusSpend {
  usedPercent: number | null;
  resetsAt: string | null;
  usedUsd: number | null;
  limitUsd: number | null;
  period: string | null;
}

/** The session's status. Every figure is Claude's; one it did not send is null, never zero. */
export interface SessionStatus {
  version: number;
  at: string;
  refreshSeconds: number | null;
  terminal: 'off' | 'minimal' | 'full';
  wrapping: boolean;
  model: string | null;
  context: StatusContext | null;
  cost: { usd: number | null; durationMs: number | null; linesAdded: number | null; linesRemoved: number | null } | null;
  limits: { fiveHour: StatusWindow | null; sevenDay: StatusWindow | null; spend: StatusSpend | null } | null;
  project: string | null;
  specialists: string[] | null;
}

/** Context used at or past this reads as a warning. */
export const CONTEXT_WARN = 70;

/** Context used at or past this reads as urgent: compaction is near. */
export const CONTEXT_URGENT = 90;

/** How a share of a window reads: fine, worth a look, or about to run out. */
export type Level = 'ok' | 'warn' | 'urgent';

export function levelOf(percent: number | null | undefined, warn = CONTEXT_WARN, urgent = CONTEXT_URGENT): Level {
  if (percent === null || percent === undefined || !Number.isFinite(percent)) {
    return 'ok';
  }

  return percent >= urgent ? 'urgent' : percent >= warn ? 'warn' : 'ok';
}

/** The words a level adds, so colour is never the only thing saying it. */
export function levelWords(level: Level): string {
  return level === 'urgent' ? 'nearly full' : level === 'warn' ? 'filling' : '';
}

/** The context share to show, rounded, or null where Claude has not said. */
export function contextPercent(status: SessionStatus | null | undefined): number | null {
  const used = status?.context?.usedPercent;

  return used === null || used === undefined || !Number.isFinite(used) ? null : Math.round(used);
}

/**
 * Whether a snapshot is too old to stand for now: past twice the interval
 * Claude was asked to redraw at. With no interval Claude redraws only on its
 * own events, so nothing can be said, and its age is shown instead.
 */
export function isStale(status: SessionStatus, now: number): boolean {
  if (!status.refreshSeconds) {
    return false;
  }

  return now - Date.parse(status.at) > status.refreshSeconds * 2000;
}

/** How long ago, in the house's short form: just now, 40s, 3m, 2h. */
export function ago(at: string, now: number): string {
  const seconds = Math.max(0, Math.round((now - Date.parse(at)) / 1000));

  if (seconds < 10) {
    return 'just now';
  }

  if (seconds < 60) {
    return `${seconds}s ago`;
  }

  const minutes = Math.floor(seconds / 60);

  return minutes < 60 ? `${minutes}m ago` : `${Math.floor(minutes / 60)}h${String(minutes % 60).padStart(2, '0')}m ago`;
}

/** How long until a window resets: in 42m, in 3h10m, in 2d. */
export function resetsIn(at: string | null, now: number): string | null {
  if (!at) {
    return null;
  }

  const minutes = Math.max(0, Math.round((Date.parse(at) - now) / 60000));

  if (minutes < 60) {
    return `resets in ${minutes}m`;
  }

  if (minutes < 48 * 60) {
    return `resets in ${Math.floor(minutes / 60)}h${String(minutes % 60).padStart(2, '0')}m`;
  }

  return `resets in ${Math.round(minutes / 1440)}d`;
}

/** Dollars as Claude estimates them: cents below ten dollars, whole dollars above. */
export function dollars(usd: number | null | undefined): string | null {
  if (usd === null || usd === undefined || !Number.isFinite(usd)) {
    return null;
  }

  return usd < 10 ? `$${usd.toFixed(2)}` : `$${Math.round(usd)}`;
}

/** Tokens, short: 840, 84k, 1.2M. */
export function tokens(count: number | null | undefined): string | null {
  if (count === null || count === undefined || !Number.isFinite(count)) {
    return null;
  }

  if (count < 1000) {
    return String(count);
  }

  return count < 1_000_000 ? `${Math.round(count / 1000)}k` : `${(count / 1_000_000).toFixed(1)}M`;
}

/** A specialist's id in the words the strip uses: the last part, as the line does. */
export function specialistName(id: string): string {
  const parts = id.split('.');

  return parts[parts.length - 1] || id;
}
