import { describe, expect, it } from 'vitest';
import { ruleCommand, ruleProblem, ruleValues, ruleWords, spanWords, valuesFromRule, type EventKind, type RuleRead } from './triggerRule';

const KINDS: EventKind[] = [
  { kind: 'run-finished', fields: ['team', 'project', 'outcome', 'run'], github: false, ending: false },
  { kind: 'pr-opened', fields: ['repo', 'base', 'trust', 'draft', 'title'], github: true, ending: false },
  { kind: 'clock', fields: ['rule'], github: false, ending: false },
];

const TEAMS = ['reviewer', 'docs-crew'];

describe('ruleWords', () => {
  it('writes a team rule with everything it was given, and leaves the defaults out', () => {
    const v = ruleValues({
      name: 'review-pulls',
      on: 'pr-opened',
      team: 'reviewer',
      goal: 'Review {title}',
      projects: ['app', 'site'],
      where: [{ field: 'base', value: 'main' }, { field: 'trust', value: ' trusted ' }],
      capUsd: '12',
      budgetUsd: 'proposed',
      debounce: '10m',
    });

    expect(ruleWords(v)).toEqual([
      'review-pulls', 'pr-opened', '--team', 'reviewer', '--goal', 'Review {title}', '--autonomy', 'autonomous',
      '--project', 'app', '--project', 'site', '--where', 'base=main', '--where', 'trust=trusted',
      '--cap-usd', '12', '--budget-usd', 'proposed', '--debounce', '10m',
    ]);
    expect(ruleCommand(v)).toBe(
      '$ loadout trigger add review-pulls pr-opened --team reviewer --goal "Review {title}" --autonomy autonomous '
        + '--project app --project site --where base=main --where trust=trusted --cap-usd 12 --budget-usd proposed --debounce 10m',
    );
  });

  it('writes a built-in rule with no money in it, a clock, the caps that differ and --disabled', () => {
    const v = ruleValues({ name: 'nightly', on: 'clock', starts: 'builtin', builtin: 'memory audit', clock: 'at', at: '02:30', capRuns: '3', window: '7d', disabled: true });

    expect(ruleWords(v)).toEqual(['nightly', 'clock', '--builtin', 'memory audit', '--at', '02:30', '--cap-runs', '3', '--window', '7d', '--disabled']);
  });
});

describe('valuesFromRule', () => {
  it('reads a rule back so saving it unchanged writes the same rule', () => {
    const rule: RuleRead = {
      id: 'after-fail',
      on: 'run-finished',
      projects: ['app'],
      where: { outcome: 'failed' },
      action: { kind: 'team', name: 'reviewer', goal: 'Why did {run} fail?', autonomy: 'supervised' },
      capUsd: 10,
      budgetUsd: 4,
      capRuns: 3,
      window: '7.00:00:00',
      debounce: '00:10:00',
      enabled: false,
      version: 2,
    };

    const v = valuesFromRule(rule);

    expect(v).toMatchObject({ name: 'after-fail', starts: 'team', team: 'reviewer', autonomy: 'supervised', capUsd: '10', budgetUsd: '4', capRuns: '3', window: '7d', debounce: '10m', disabled: true });
    expect(ruleWords(v)).toEqual([
      'after-fail', 'run-finished', '--team', 'reviewer', '--goal', 'Why did {run} fail?', '--autonomy', 'supervised',
      '--project', 'app', '--where', 'outcome=failed', '--cap-usd', '10', '--budget-usd', '4', '--cap-runs', '3', '--window', '7d', '--debounce', '10m', '--disabled',
    ]);
  });

  it('reads a clock rule’s interval and time', () => {
    const base = { id: 'c', on: 'clock', projects: [], where: {}, action: { kind: 'builtin', name: 'docs audit', goal: '', autonomy: 'autonomous' }, capRuns: 10, window: '1.00:00:00', debounce: '00:00:00', enabled: true, version: 1 };

    expect(valuesFromRule({ ...base, every: '01:30:00' })).toMatchObject({ clock: 'every', every: '90m', window: '24h', debounce: '' });
    expect(valuesFromRule({ ...base, at: '09:15:00' })).toMatchObject({ clock: 'at', at: '09:15' });
  });

  it('turns a .NET span into the words the command takes', () => {
    expect(spanWords('2.00:00:00')).toBe('2d');
    expect(spanWords('06:00:00')).toBe('6h');
    expect(spanWords('00:45:00')).toBe('45m');
    expect(spanWords('00:00:00')).toBe('');
    expect(spanWords(null)).toBe('');
  });
});

describe('ruleProblem', () => {
  const good = ruleValues({ name: 'review-pulls', on: 'pr-opened', team: 'reviewer', goal: 'Review it' });

  it('lets a whole rule through', () => {
    expect(ruleProblem(good, KINDS, TEAMS)).toBeNull();
  });

  it.each([
    [{ name: '' }, 'give the rule a name first'],
    [{ name: 'Not OK' }, 'lowercase words joined by hyphens'],
    [{ on: 'nonsense' }, 'pick what it waits for'],
    [{ team: 'ghost' }, 'there is no team called ghost'],
    [{ goal: ' ' }, 'say what to ask the team for'],
    [{ where: [{ field: 'outcome', value: 'failed' }] }, 'a pr-opened event carries no outcome'],
    [{ where: [{ field: 'base', value: '' }] }, 'say what base must be'],
    [{ capUsd: '' }, 'needs a spend cap'],
    [{ capUsd: '0' }, 'above nothing'],
    [{ capUsd: '5', budgetUsd: '9' }, 'cannot be more than the spend cap'],
    [{ capRuns: '0' }, 'at least one'],
    [{ window: 'a week' }, '24h or 7d'],
    [{ debounce: 'soon' }, '10m or 1h'],
  ])('refuses %j', (change, said) => {
    expect(ruleProblem({ ...good, ...change }, KINDS, TEAMS)).toContain(said);
  });

  it('holds a clock rule to its interval and to one project when it starts a team', () => {
    const clock = { ...good, on: 'clock', projects: ['app'] };

    expect(ruleProblem({ ...clock, every: '2m' }, KINDS, TEAMS)).toContain('five minutes');
    expect(ruleProblem({ ...clock, every: 'often' }, KINDS, TEAMS)).toContain('30m, 2h or 1d');
    expect(ruleProblem({ ...clock, projects: [] }, KINDS, TEAMS)).toContain('the one project');
    expect(ruleProblem(clock, KINDS, TEAMS)).toBeNull();
  });

  it('asks no money of a built-in', () => {
    expect(ruleProblem({ ...good, starts: 'builtin', builtin: 'docs audit', capUsd: '' }, KINDS, TEAMS)).toBeNull();
  });
});
