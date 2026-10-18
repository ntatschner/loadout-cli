// Findings from drift, repo check, doctor and the audits, in one shape: a
// severity read case-insensitively, whatever the command spelled it as, and
// worst first.

export type Severity = 'error' | 'warning' | 'info' | 'ok';

export interface Finding {
  name?: string;
  severity?: string;
  detail?: string;
  fixable?: boolean;
}

const RANK: Record<Severity, number> = { error: 0, warning: 1, info: 2, ok: 3 };

/** Error, Warning, Info and fine, as the commands write them, lower-cased; anything else is info. */
export function severityOf(value: string | undefined | null): Severity {
  const word = String(value ?? 'info').toLowerCase();

  if (word === 'error' || word === 'bad') {
    return 'error';
  }

  if (word === 'warning' || word === 'warn') {
    return 'warning';
  }

  if (word === 'ok' || word === 'fine' || word === 'pass' || word === 'passed') {
    return 'ok';
  }

  return 'info';
}

/** Worst first; findings of one severity keep the order they came in. */
export function worstFirst<T extends Finding>(findings: T[]): T[] {
  return findings
    .map((finding, index) => ({ finding, index }))
    .sort((a, b) => RANK[severityOf(a.finding.severity)] - RANK[severityOf(b.finding.severity)] || a.index - b.index)
    .map((one) => one.finding);
}

/** "2 errors and 1 warning; 2 can be put right.", or nothing when there is neither. */
export function summaryOf(findings: Finding[]): string {
  const errors = findings.filter((one) => severityOf(one.severity) === 'error').length;
  const warnings = findings.filter((one) => severityOf(one.severity) === 'warning').length;
  const fixable = findings.filter((one) => one.fixable).length;

  if (!errors && !warnings) {
    return '';
  }

  const parts = [errors ? `${errors} error${errors === 1 ? '' : 's'}` : '', warnings ? `${warnings} warning${warnings === 1 ? '' : 's'}` : ''].filter(Boolean);

  return `${parts.join(' and ')}${fixable ? `; ${fixable} can be put right.` : '.'}`;
}
