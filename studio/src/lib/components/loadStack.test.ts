// LoadStack: within, near or over the budget in words, segments that fill
// exactly the share of the bar the total uses, and a segment and its row
// lit together.
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import LoadStack from './LoadStack.svelte';
import { stackOf } from '../loadStack';

const SELECTED = [
  { id: 'language.csharp', title: 'C#', reason: '2588 .cs files', estimatedTokens: 300 },
  { id: 'foundation.verification', title: 'Verification', reason: 'always applies', estimatedTokens: 200, always: true },
  { id: 'mode.implement', title: 'Implement', reason: 'implement mode', estimatedTokens: 100 },
];

describe('LoadStack', () => {
  it('says within, near or over the budget', () => {
    expect(stackOf(SELECTED, 600, 1000).standing).toBe('within');
    expect(stackOf(SELECTED, 900, 1000, true).standing).toBe('near');
    expect(stackOf(SELECTED, 1200, 1000).standing).toBe('over');

    render(LoadStack, { selected: SELECTED, estimatedTokens: 1200, tokenBudget: 1000 });
    expect(screen.getByText('· 200 over the budget of 1,000')).toBeTruthy();
  });

  it('fills exactly the used share, always-loaded first', () => {
    for (const [used, budget] of [[600, 1000], [1200, 1000], [700, 0]]) {
      const stack = stackOf(SELECTED, used, budget || undefined);
      const sum = stack.widths.reduce((total, one) => total + one, 0);

      expect(sum).toBeCloseTo(stack.usedShare, 6);
    }

    expect(stackOf(SELECTED, 600, 1000).usedShare).toBeCloseTo(60, 6);
    expect(stackOf(SELECTED, 600, 1000).rows.map((one) => one.id)).toEqual(['foundation.verification', 'language.csharp', 'mode.implement']);
  });

  it('lights a segment and its row together, by hover or by focus', async () => {
    const shown = render(LoadStack, { selected: SELECTED, estimatedTokens: 600, tokenBudget: 1000 });
    const rows = () => [...shown.container.querySelectorAll('ol li')];
    const segment = screen.getByRole('button', { name: 'C#, 300 tokens' });

    await fireEvent.focus(segment);
    expect(segment.classList.contains('lit')).toBe(true);
    expect(rows().map((row) => row.classList.contains('lit'))).toEqual([false, true, false]);

    await fireEvent.blur(segment);
    await fireEvent.mouseEnter(rows()[2]);
    expect(screen.getByRole('button', { name: 'Implement, 100 tokens' }).classList.contains('lit')).toBe(true);
  });
});
