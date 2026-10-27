// The readings every place that draws a session's status shares: when the
// context reads as filling or nearly full, when a snapshot is too old to
// stand for now, and how the figures are written.
import { describe, expect, it } from 'vitest';
import { ago, CONTEXT_URGENT, CONTEXT_WARN, contextPercent, dollars, isStale, levelOf, resetsIn, tokens } from './status';
import { snapshot } from './statusFixture';

const at = Date.parse('2026-10-27T12:00:00Z');

describe('the context levels', () => {
  it('turn to warning and then urgent exactly at the chosen thresholds', () => {
    expect(CONTEXT_WARN).toBe(70);
    expect(CONTEXT_URGENT).toBe(90);
    expect(levelOf(69.9)).toBe('ok');
    expect(levelOf(70)).toBe('warn');
    expect(levelOf(89.9)).toBe('warn');
    expect(levelOf(90)).toBe('urgent');
    expect(levelOf(null)).toBe('ok');
  });

  it('read nothing as a figure where Claude sent none', () => {
    expect(contextPercent(snapshot({ context: null }))).toBeNull();
    expect(contextPercent(snapshot({ context: { ...snapshot().context!, usedPercent: null } }))).toBeNull();
    expect(contextPercent(null)).toBeNull();
    expect(contextPercent(snapshot())).toBe(42);
  });
});

describe('an old snapshot', () => {
  it('is old past twice the redraw interval, and never where there is none', () => {
    expect(isStale(snapshot(), at + 59_000)).toBe(false);
    expect(isStale(snapshot(), at + 61_000)).toBe(true);
    expect(isStale(snapshot({ refreshSeconds: null }), at + 3_600_000)).toBe(false);
  });

  it('says how old it is', () => {
    expect(ago('2026-10-27T12:00:00Z', at + 5_000)).toBe('just now');
    expect(ago('2026-10-27T12:00:00Z', at + 40_000)).toBe('40s ago');
    expect(ago('2026-10-27T12:00:00Z', at + 180_000)).toBe('3m ago');
    expect(ago('2026-10-27T12:00:00Z', at + 7_500_000)).toBe('2h05m ago');
  });
});

describe('the figures', () => {
  it('are written short', () => {
    expect(dollars(1.234)).toBe('$1.23');
    expect(dollars(42.6)).toBe('$43');
    expect(dollars(null)).toBeNull();
    expect(tokens(840)).toBe('840');
    expect(tokens(84000)).toBe('84k');
    expect(tokens(1_200_000)).toBe('1.2M');
    expect(resetsIn('2026-10-27T14:10:00Z', at)).toBe('resets in 2h10m');
    expect(resetsIn('2026-10-27T12:42:00Z', at)).toBe('resets in 42m');
    expect(resetsIn(null, at)).toBeNull();
  });
});
