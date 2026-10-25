import { describe, expect, it } from 'vitest';
import { choiceFor, gotTo, notReady, resolveLine, resolveWords, type BlockedRead } from './blocked';

const limited: BlockedRead = {
  id: '20261025-0900-ab12',
  team: 'iterating-project',
  project: 'loadout-cli',
  task: 'fix-login',
  goal: 'Fix the login',
  kind: 'limited',
  ended: 'budget spent: 4.80 of 5.00 USD',
  cost: 4.8,
  budget: 5,
  rounds: 3,
  roundLimit: 6,
  origin: null,
  follows: null,
  chain: 1,
  merged: ['teams/r/implementer-1'],
  unmerged: ['teams/r/implementer-2'],
  lead: null,
  suggested: { action: 'raise', label: 'Raise the limit and carry on', reason: 'It ran out of money.', rule: 'budget' },
  offered: [
    { action: 'raise', label: 'Raise the limit and carry on' },
    { action: 'carry-on', label: 'Carry on now' },
    { action: 'set-aside', label: 'Set aside' },
  ],
  raise: { cap: 'budget', figure: 5, used: 4.8, said: '$5.00 more; it spent $4.80 of $5.00' },
};

describe('blocked runs', () => {
  it('starts from the suggestion, with a raise filled in as the same again', () => {
    const choice = choiceFor(limited);

    expect(choice.action).toBe('raise');
    expect(choice.figure).toBe('5.00');
    expect(notReady(limited, choice)).toBeNull();
  });

  it('sends the edited figure for the cap the run ran out of', () => {
    const words = resolveWords(limited, { ...choiceFor(limited), figure: '$7.5' });

    expect(words).toEqual(['20261025-0900-ab12', '--action', 'raise', '--usd=7.5']);

    const rounds: BlockedRead = { ...limited, raise: { cap: 'rounds', figure: 6, used: 6, said: '' } };

    expect(resolveWords(rounds, { ...choiceFor(rounds), figure: '4' })).toContain('--rounds=4');
    expect(notReady(rounds, { ...choiceFor(rounds), figure: '2.5' })).toBe('rounds are a whole number');
  });

  it('joins free text with = so an answer starting with a dash is not read as an option', () => {
    const words = resolveWords(limited, { ...choiceFor(limited), action: 'carry-on', answer: '-use the staging db' });

    expect(words).toEqual(['20261025-0900-ab12', '--action', 'carry-on', '--answer=-use the staging db']);
    expect(resolveLine(limited, { ...choiceFor(limited), action: 'carry-on', answer: 'use staging' })).toBe(
      '$ loadout team blocked resolve 20261025-0900-ab12 --action carry-on "--answer=use staging"',
    );
  });

  it('will not confirm an edit that changes nothing, or an action the run does not offer', () => {
    const edit = { ...choiceFor(limited), action: 'edit-requeue' as const };
    const offering: BlockedRead = { ...limited, offered: [...limited.offered, { action: 'edit-requeue', label: 'Edit the task, then re-queue' }] };

    expect(notReady(limited, edit)).toBe('that is not open to this run');
    expect(notReady(offering, edit)).toBe('say what changes: the title, the detail or the done-when');
    expect(resolveWords(offering, { ...edit, doneWhen: 'tests pass\n\n  login works  ' })).toEqual([
      '20261025-0900-ab12',
      '--action',
      'edit-requeue',
      '--done-when=tests pass',
      '--done-when=login works',
    ]);
  });

  it('says how far the run got', () => {
    expect(gotTo(limited)).toBe('3 of 6 round(s) · $4.80 of $5.00 · 1 merged, 1 left on branches');
  });
});
