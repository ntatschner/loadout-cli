import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { hadSecret, keepTail, KEPT, redact } from './redact';

// The same file SecretRedactorTests reads: the two redactors agree case for case.
const shared = JSON.parse(readFileSync('../tests/shared/redaction-cases.json', 'utf-8')) as {
  cases: { name: string; input: string | null; expected: string }[];
};

describe('redact, held to the shared cases', () => {
  it('has cases to hold it to', () => {
    expect(shared.cases.length).toBeGreaterThan(40);
  });

  for (const one of shared.cases) {
    it(one.name, () => {
      expect(redact(one.input)).toBe(one.expected);
    });
  }
});

describe('the cap', () => {
  it('reads only the last KEPT characters, from the start of a line', () => {
    const long = `${'x'.repeat(KEPT)}\nTOKEN=abc`;
    const kept = keepTail(long);

    expect(kept.cut).toBe(true);
    expect(kept.text).toBe('TOKEN=abc');
    expect(redact(long)).toBe('TOKEN=[redacted]');
  });

  it('leaves a short text whole', () => {
    expect(keepTail('one\ntwo')).toEqual({ text: 'one\ntwo', cut: false });
  });

  it('finishes quickly on a long run of an unclosed quote', () => {
    const started = performance.now();
    redact(`--password "${'a`'.repeat(KEPT)}`);

    expect(performance.now() - started).toBeLessThan(1000);
  });
});

describe('hadSecret', () => {
  it('says whether anything was taken out', () => {
    expect(hadSecret('loadout login --token abc')).toBe(true);
    expect(hadSecret('loadout team run --json')).toBe(false);
    expect(hadSecret(null)).toBe(false);
  });
});
