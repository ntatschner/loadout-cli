// What went wrong, in one shape the Problem toast shows: a one-line cause,
// and behind Details the exit code or HTTP status, the command and the end of
// the output. Every Problem in Studio is built here, so the wording cannot
// drift between pages, and everything is redacted before it leaves.
//
// The cause comes from what the failure itself says where it says it plainly:
// the --json error document a command writes ({"error": ..., "exitCode": n}),
// or the daemon's own error body. Where neither exists, the cause is picked
// out of standard error: the last line that reads as an error, or else the
// last line that says anything.
import { JobFailed } from './api';
import { hadSecret, keepTail, redact } from './redact';

export { JobFailed, StillGoing } from './api';

export type CauseSource = 'structured' | 'stderr' | 'message';

export interface Failure {
  /** One line, redacted. */
  cause: string;
  source: CauseSource;
  exitCode: number | null;
  /** The HTTP status, where the daemon refused. */
  status: number | null;
  /** The command, redacted. */
  command: string;
  /** Whether redacting the command took a secret out of it, which Copy command says. */
  commandRedacted: boolean;
  /** The end of the output, standard output then standard error, redacted. */
  output: string;
  /** Studio kept only the last part of the output. */
  cut: boolean;
  /** The daemon cut the output before Studio saw it. */
  daemonCut: boolean;
  /** The causes of the attempts that failed before this one, oldest first. */
  earlier: string[];
}

/** The longest a cause line runs before it is cut short. */
const CAUSE_LONGEST = 240;

/** A line that reads as an error rather than as progress. */
const ERRORISH = /^(?:error|fatal|failed|exception|unhandled|refused|denied)\b|\b(?:error|exception)\s*:/i;

function oneLine(text: string): string {
  const line = text.replace(/\s+/g, ' ').trim();

  return line.length > CAUSE_LONGEST ? `${line.slice(0, CAUSE_LONGEST - 1)}…` : line;
}

/** The error a --json document carries, from the whole text or from its last line that parses as one. */
export function structuredError(text: string | null | undefined): string | null {
  if (!text?.trim()) {
    return null;
  }

  // The whole text; then, since a command prints the document indented over
  // several lines and may have written other lines first, everything from the
  // last line that opens one; then each line on its own.
  const whole = text.trim();
  const opened = whole.lastIndexOf('\n{');
  const candidates = [whole, ...(opened >= 0 ? [whole.slice(opened + 1)] : []), ...whole.split(/\r?\n/).reverse()];

  for (const candidate of candidates) {
    const trimmed = candidate.trim();

    if (!trimmed.startsWith('{')) {
      continue;
    }

    try {
      const read = JSON.parse(trimmed) as { error?: unknown };

      if (typeof read?.error === 'string' && read.error.trim()) {
        return read.error.trim();
      }
    } catch {
      // Not a document; the next line may be.
    }
  }

  return null;
}

/** The line of standard error that best says why: the last that reads as an error, or else the last that says anything. */
export function stderrCause(text: string | null | undefined): string | null {
  const lines = (text ?? '')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line && !/^[─━═\-=*~_\s]+$/.test(line));

  if (!lines.length) {
    return null;
  }

  return [...lines].reverse().find((line) => ERRORISH.test(line)) ?? lines[lines.length - 1];
}

function joined(output: string | null | undefined, error: string | null | undefined): string {
  return [output?.trimEnd(), error?.trimEnd()].filter(Boolean).join('\n');
}

/**
 * Builds the failure a Problem toast shows from whatever was thrown, with
 * the command the button ran. A SignedOut is handled by the caller first.
 */
export function describe(error: unknown, command = ''): Failure {
  const base = {
    command: redact(command),
    commandRedacted: hadSecret(command),
    exitCode: null as number | null,
    status: null as number | null,
    output: '',
    cut: false,
    daemonCut: false,
    earlier: [] as string[],
  };

  if (error instanceof JobFailed) {
    const { job } = error;
    const all = joined(job.output, job.error);
    const kept = keepTail(all);
    const fromJson = structuredError(job.output) ?? structuredError(job.error);
    const fromStderr = fromJson ? null : stderrCause(job.error) ?? stderrCause(job.output);
    const cause = fromJson ?? fromStderr ?? `'loadout ${job.path}' ended with exit code ${job.exitCode ?? 'unknown'}.`;

    return {
      ...base,
      command: redact(command || `loadout ${job.line}`),
      commandRedacted: hadSecret(command || `loadout ${job.line}`),
      cause: oneLine(redact(cause)),
      source: fromJson ? 'structured' : fromStderr ? 'stderr' : 'message',
      exitCode: job.exitCode,
      output: redact(kept.text),
      cut: kept.cut,
      daemonCut: job.truncated,
    };
  }

  const status = typeof (error as { status?: unknown })?.status === 'number' ? (error as { status: number }).status : null;
  const body = typeof (error as { body?: unknown })?.body === 'string' ? (error as { body: string }).body : '';
  const message = error instanceof Error ? error.message : String(error ?? '');
  const fromBody = structuredError(body);
  const kept = keepTail(body);
  const firstLine = message.split(/\r?\n/).find((line) => line.trim()) ?? '';

  return {
    ...base,
    cause: oneLine(redact(fromBody ?? (firstLine || 'It failed without saying why.'))),
    source: fromBody ? 'structured' : 'message',
    status,
    output: redact(kept.text || (message.includes('\n') ? message : '')),
    cut: kept.cut,
  };
}

/** The failure as plain text, for Copy details: what someone pastes into an issue. */
export function detailsText(failure: Failure): string {
  const lines = [`Cause: ${failure.cause}`];

  if (failure.exitCode !== null) {
    lines.push(`Exit code: ${failure.exitCode}`);
  }

  if (failure.status !== null) {
    lines.push(`HTTP status: ${failure.status}`);
  }

  if (failure.command) {
    lines.push(`Command: ${failure.command}`);
  }

  failure.earlier.forEach((cause, index) => lines.push(`Attempt ${index + 1}: ${cause}`));

  const note = truncatedNote(failure);

  if (note) {
    lines.push(note);
  }

  if (failure.output) {
    lines.push('', failure.output);
  }

  return lines.join('\n');
}

/** What was left out of the output, in words, or null when nothing was. */
export function truncatedNote(failure: Failure): string | null {
  const parts: string[] = [];

  if (failure.daemonCut) {
    parts.push('the daemon cut the output at 2 MiB');
  }

  if (failure.cut) {
    parts.push('Studio kept the last 64 KiB');
  }

  if (!parts.length) {
    return null;
  }

  const said = parts.join(', and ');

  return `${said.charAt(0).toUpperCase()}${said.slice(1)}.`;
}
