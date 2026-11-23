// The describer over the shapes a failure really arrives in. The --json error
// and the bare stderr line are what 'loadout task show no-such-task-xyz' wrote
// on 23 Nov 2026, with and without --json.
import { describe as group, expect, it } from 'vitest';
import { JobFailed, Refused, StillGoing, type Job } from './api';
import { describe, detailsText, stderrCause, structuredError, truncatedNote } from './failure';
import { KEPT } from './redact';

function job(over: Partial<Job>): Job {
  return {
    id: 'j1',
    path: 'task show',
    line: 'task show no-such-task-xyz',
    state: 'failed',
    exitCode: 2,
    startedAt: '2026-11-23T01:00:00Z',
    endedAt: '2026-11-23T01:00:01Z',
    truncated: false,
    output: '',
    error: '',
    ...over,
  };
}

const JSON_ERROR = '{\n  "error": "\\u0027no-such-task-xyz\\u0027 is not a task of loadout-cli.",\n  "exitCode": 2\n}\n';

group('the cause', () => {
  it('comes from the --json error document where the command wrote one', () => {
    const failure = describe(new JobFailed(job({ output: JSON_ERROR }), 'x'));

    expect(failure.cause).toBe("'no-such-task-xyz' is not a task of loadout-cli.");
    expect(failure.source).toBe('structured');
    expect(failure.exitCode).toBe(2);
  });

  it('finds the document after other lines of output', () => {
    expect(structuredError(`Reading the tasks\n${JSON_ERROR}`)).toBe("'no-such-task-xyz' is not a task of loadout-cli.");
  });

  it('comes from standard error where there is no document', () => {
    const failure = describe(new JobFailed(job({ error: "'no-such-task-xyz' is not a task of loadout-cli.\n" }), 'x'));

    expect(failure.cause).toBe("'no-such-task-xyz' is not a task of loadout-cli.");
    expect(failure.source).toBe('stderr');
  });

  it('prefers the last line that reads as an error over progress written after it', () => {
    expect(stderrCause('Fetching origin\nfatal: could not read Username\nCleaning up\n')).toBe('fatal: could not read Username');
  });

  it('takes the last line that says anything when none reads as an error, skipping rules', () => {
    expect(stderrCause('first\nThe branch is behind.\n────────\n\n')).toBe('The branch is behind.');
  });

  it('says the exit code when the command said nothing at all', () => {
    const failure = describe(new JobFailed(job({ exitCode: 7 }), 'x'));

    expect(failure.cause).toBe("'loadout task show' ended with exit code 7.");
    expect(failure.source).toBe('message');
  });

  it("comes from the daemon's error body where it refused, with the status", () => {
    const failure = describe(new Refused('Studio is only watching.', 403, '{"error":"Studio is only watching."}'), 'loadout x');

    expect(failure.cause).toBe('Studio is only watching.');
    expect(failure.source).toBe('structured');
    expect(failure.status).toBe(403);
    expect(failure.exitCode).toBeNull();
  });

  it('is one line, cut short when it runs long', () => {
    const failure = describe(new Error(`${'word '.repeat(100)}\nsecond line`));

    expect(failure.cause).not.toContain('\n');
    expect(failure.cause.length).toBeLessThanOrEqual(240);
    expect(failure.cause.endsWith('…')).toBe(true);
  });
});

group('redaction', () => {
  it('takes secrets out of the cause, the command and the output', () => {
    const failure = describe(
      new JobFailed(job({ error: 'fatal: https://me:ghp_abcdefghijklmnop@github.com/o/r.git refused\n', output: 'TOKEN=abc123\n' }), 'x'),
      'loadout login --token abc123',
    );

    expect(failure.cause).toBe('fatal: https://[redacted]@github.com/o/r.git refused');
    expect(failure.command).toBe('loadout login --token [redacted]');
    expect(failure.commandRedacted).toBe(true);
    expect(failure.output).not.toContain('abc123');
    expect(failure.output).not.toContain('ghp_');
    expect(detailsText(failure)).not.toMatch(/abc123|ghp_/);
  });

  it('says when the command had nothing to take out', () => {
    expect(describe(new Error('x'), 'loadout team run --json').commandRedacted).toBe(false);
  });
});

group('the output', () => {
  it('keeps only the last 64 KiB and says so, with the daemon\'s own cut', () => {
    const failure = describe(new JobFailed(job({ output: `${'x'.repeat(KEPT)}\nlast line`, truncated: true }), 'x'));

    expect(failure.cut).toBe(true);
    expect(failure.daemonCut).toBe(true);
    expect(failure.output.length).toBeLessThanOrEqual(KEPT);
    expect(failure.output.endsWith('last line')).toBe(true);
    expect(truncatedNote(failure)).toBe('The daemon cut the output at 2 MiB, and Studio kept the last 64 KiB.');
  });

  it('notes nothing when nothing was cut', () => {
    expect(truncatedNote(describe(new JobFailed(job({ output: 'short' }), 'x')))).toBeNull();
  });
});

group('copy details', () => {
  it('carries the cause, the code, the command and the output', () => {
    const text = detailsText(describe(new JobFailed(job({ error: 'Error: it broke\n' }), 'x'), 'loadout task show a'));

    expect(text).toBe('Cause: Error: it broke\nExit code: 2\nCommand: loadout task show a\n\nError: it broke');
  });
});

it('a still-going command is its own kind, not a failure', () => {
  expect(new StillGoing('still going')).toBeInstanceOf(Error);
  expect(new JobFailed(job({}), 'x')).toBeInstanceOf(Refused);
});
