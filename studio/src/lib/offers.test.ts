import { describe, expect, it } from 'vitest';
import { acceptOfferWords, environmentNames, firstOpen, gapWord, nextStack, stackIn, type Offer } from './offers';

const offer = (over: Partial<Offer> = {}): Offer => ({
  check: 'ci',
  name: 'CI',
  state: 'gap',
  evidence: 'No workflow.',
  canOffer: true,
  stacks: ['all', 'dotnet', 'node', 'generic'],
  environments: [],
  prerequisite: null,
  linked: false,
  ...over,
});

describe('the offer one key takes', () => {
  it('passes over one already linked, one with no template and one put off', () => {
    const offers = [offer({ check: 'a', linked: true }), offer({ check: 'b', canOffer: false }), offer({ check: 'c' }), offer({ check: 'd' })];

    expect(firstOpen(offers, ['c'])?.check).toBe('d');
    expect(firstOpen(offers, ['c', 'd'])).toBeNull();
  });
});

describe('the stack key', () => {
  it('cycles every choice and comes back round to the first', () => {
    const seen: (string | null)[] = [];
    const chosen: Record<string, string> = {};

    for (let press = 0; press < 4; press++) {
      const next = nextStack(offer(), chosen)!;

      seen.push(next);
      chosen.ci = next;
    }

    expect(seen).toEqual(['dotnet', 'node', 'generic', 'all']);
  });

  it('has nothing to cycle on a check with no stacks', () => {
    expect(nextStack(offer({ stacks: [] }), {})).toBeNull();
    expect(stackIn(offer({ stacks: [] }), {})).toBeNull();
  });
});

describe('the words that take an offer up', () => {
  it('leave a default out, so the common case is the shortest line', () => {
    expect(acceptOfferWords('notes', ['--project', 'p'], offer(), {})).toEqual(['notes', '--project', 'p', '--accept=ci']);
  });

  it('name a stack only once it has been cycled away from the default', () => {
    expect(acceptOfferWords('notes', [], offer(), { ci: 'node' })).toEqual(['notes', '--accept=ci', '--stack=node']);
  });

  it('name environments only when they differ from those offered', () => {
    const named = offer({ check: 'environments', stacks: [], environments: ['dev', 'production'] });

    expect(acceptOfferWords('notes', [], named, {}, 'dev, production')).toEqual(['notes', '--accept=environments']);
    expect(acceptOfferWords('notes', [], named, {}, 'staging live')).toEqual(['notes', '--accept=environments', '--env-name=staging', '--env-name=live']);
  });
});

describe('environment names', () => {
  it('split on commas and spaces and lose the empties', () => {
    expect(environmentNames(' dev,  production ,,')).toEqual(['dev', 'production']);
  });
});

describe('the count on the shelf', () => {
  it('says nothing for checks nobody could look at', () => {
    expect(gapWord({ seen: 0, noOffer: 0, unseen: 3 })).toBeNull();
    expect(gapWord(null)).toBeNull();
  });

  it('counts seen gaps and marks those with no offer', () => {
    expect(gapWord({ seen: 1, noOffer: 0, unseen: 2 })).toBe('1 gap');
    expect(gapWord({ seen: 2, noOffer: 1, unseen: 0 })).toBe('2 gaps, 1 no offer');
  });
});
