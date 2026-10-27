// A session's status as the daemon serves it, for tests to change one part of.
import type { SessionStatus } from './status';

export const snapshot = (over: Partial<SessionStatus> = {}): SessionStatus => ({
  version: 1,
  at: '2026-10-27T12:00:00Z',
  refreshSeconds: 30,
  terminal: 'off',
  wrapping: false,
  model: 'Opus 5.5',
  context: { usedPercent: 42, inputTokens: 84000, windowSize: 200000, uncachedTokens: 1200, cacheReadTokens: 79800, cacheWriteTokens: 3000, outputTokens: 900 },
  cost: { usd: 1.234, durationMs: 600000, linesAdded: 40, linesRemoved: 7 },
  limits: { fiveHour: { usedPercent: 61.5, resetsAt: '2026-10-27T14:10:00Z' }, sevenDay: null, spend: null },
  project: 'demo',
  specialists: ['role.implementer', 'language.csharp'],
  ...over,
});
