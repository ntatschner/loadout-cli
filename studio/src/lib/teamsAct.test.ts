// What the page sends when a question is answered: the daemon reads the
// fields by name, so a field it does not read arrives as nothing and the
// page would still say it was done. Think again is a flag sent with no
// answer beside it, since the command refuses the pair.
import { describe, expect, it, vi } from 'vitest';

const sent: { run: string; verb: string; body: Record<string, unknown> }[] = [];
const said: string[] = [];

vi.mock('./api', async (original) => ({
  ...(await original<typeof import('./api')>()),
  runs: {
    act: vi.fn(async (run: string, verb: string, body: Record<string, unknown>) => {
      sent.push({ run, verb, body });

      return 'Done.';
    }),
  },
}));

vi.mock('./state.svelte', () => ({
  studio: {
    perform: async (work: () => Promise<unknown>, _done: string, cmd: string) => {
      said.push(cmd);
      await work();

      return true;
    },
  },
}));

const { answerGate } = await import('./teamsAct');

describe('answerGate', () => {
  it('sends an answer with its reason, and shows the same command', async () => {
    sent.length = 0;
    said.length = 0;
    await answerGate('r1', 'g1', { answer: 'Ship it', reason: 'the tests pass' });

    expect(sent).toEqual([{ run: 'r1', verb: 'gate', body: { gate: 'g1', answer: 'Ship it', reason: 'the tests pass' } }]);
    expect(said).toEqual(['$ loadout team gate r1 --gate g1 --answer "Ship it" --by dashboard --reason "the tests pass"']);
  });

  it('sends a changed brief as instead', async () => {
    sent.length = 0;
    await answerGate('r1', 'g2', { answer: 'yes', instead: 'Fix the README.' });

    expect(sent[0].body).toEqual({ gate: 'g2', answer: 'yes', instead: 'Fix the README.' });
  });

  it('sends Think again as a flag, with no answer beside it', async () => {
    sent.length = 0;
    said.length = 0;
    await answerGate('r1', 'g3', { answer: 'Think again', thinkAgain: true });

    expect(sent[0].body).toEqual({ gate: 'g3', thinkAgain: true });
    expect(said[0]).toBe('$ loadout team gate r1 --gate g3 --think-again --by dashboard');
  });
});
