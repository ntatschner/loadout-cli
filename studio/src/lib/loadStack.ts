// What a session would start with, as shares of one bar: each loaded
// specialist's segment, the budget's mark, and whether the total is within,
// near or over it.

export interface Loaded {
  id: string;
  title: string;
  reason?: string;
  estimatedTokens: number;
  always?: boolean;
}

export interface Stack {
  /** Always-loaded first, then the rest by size. */
  rows: Loaded[];
  used: number;
  budget: number;
  standing: 'within' | 'near' | 'over' | 'none';
  /** Each row's share of the bar, in percent, summing to the used share. */
  widths: number[];
  usedShare: number;
  budgetShare: number;
}

export function stackOf(selected: Loaded[], estimatedTokens: number | undefined, tokenBudget: number | undefined, nearBudget = false): Stack {
  const rows = [...selected].sort((a, b) => Number(!!b.always) - Number(!!a.always) || b.estimatedTokens - a.estimatedTokens);
  const sum = rows.reduce((total, one) => total + one.estimatedTokens, 0);
  const used = estimatedTokens ?? sum;
  const budget = tokenBudget ?? 0;
  const max = Math.max(used, budget) || 1;
  const usedShare = (used / max) * 100;
  // Scaled so the segments fill exactly the used share, whatever the estimate adds beyond the rows.
  const widths = rows.map((one) => (sum ? (one.estimatedTokens / sum) * usedShare : 0));
  const standing = !budget ? 'none' : used > budget ? 'over' : nearBudget ? 'near' : 'within';

  return { rows, used, budget, standing, widths, usedShare, budgetShare: budget ? (budget / max) * 100 : 0 };
}
