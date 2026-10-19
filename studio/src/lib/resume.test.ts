import { describe, expect, it } from 'vitest';
import type { Conversation, LostSession, Resume, Resumed, Session } from './api';
import { carriedWords, hostedIn, resumeAllWords, resumeConversation, type ResumeAsk } from './resume';

function conversation(over: Partial<Conversation> = {}): Conversation {
  return {
    agent: 'claude',
    id: 'abc-123',
    title: 'Fix the importer',
    project: 'alpha',
    registered: true,
    directory: '/repos/alpha',
    branch: 'main',
    lastActive: '2026-10-01T10:00:00Z',
    launchedFor: null,
    running: 'none',
    ...over,
  };
}

const session = { id: 'a1b2c3d4' } as Session;

/** A daemon that answers each resume in turn, and remembers what it was sent. */
function daemon(...answers: Resumed[]) {
  const sent: Resume[] = [];

  return {
    sent,
    resume: (resume: Resume) => {
      sent.push({ ...resume });

      return Promise.resolve(answers.shift() ?? { started: session });
    },
  };
}

function asker(answer: boolean) {
  const asked: ResumeAsk[] = [];

  return { asked, ask: (question: ResumeAsk) => (asked.push(question), Promise.resolve(answer)) };
}

describe('resuming a conversation', () => {
  it('goes to the terminal already running it rather than starting another', async () => {
    const d = daemon();

    expect(await resumeConversation(conversation({ running: 'hosted:a1b2c3d4' }), { ...d, ...asker(true) })).toEqual({ goTo: 'a1b2c3d4' });
    expect(d.sent).toEqual([]);
  });

  it('goes there too when the daemon finds it hosted', async () => {
    const d = daemon({ conflict: 'hosted', session: 'ffff0000' });

    expect(await resumeConversation(conversation(), { ...d, ...asker(true) })).toEqual({ goTo: 'ffff0000' });
  });

  it('asks before resuming one that looks to be running elsewhere, and says so when it tries again', async () => {
    const d = daemon({ conflict: 'elsewhere' });
    const a = asker(true);

    expect(await resumeConversation(conversation(), { ...d, ...a })).toEqual({ started: session });
    expect(a.asked.map((q) => q.kind)).toEqual(['elsewhere']);
    expect(d.sent[1].confirmRunning).toBe(true);
  });

  it('starts nothing when the person says no', async () => {
    const d = daemon({ conflict: 'elsewhere' });

    expect(await resumeConversation(conversation(), { ...d, ...asker(false) })).toEqual({ declined: true });
    expect(d.sent).toHaveLength(1);
  });

  it('registers only the folder the daemon named, once the person has seen it', async () => {
    const d = daemon({ conflict: 'unregistered', directory: '/somewhere/else' });
    const a = asker(true);

    await resumeConversation(conversation({ project: null, registered: false }), { ...d, ...a });

    expect(a.asked).toEqual([{ kind: 'unregistered', conversation: expect.anything(), directory: '/somewhere/else' }]);
    expect(d.sent[1].registerFolder).toBe('/somewhere/else');
  });

  it('does not ask the same question twice', async () => {
    const d = daemon({ conflict: 'elsewhere' }, { conflict: 'elsewhere' });
    const a = asker(true);

    expect(await resumeConversation(conversation(), { ...d, ...a })).toEqual({ declined: true });
    expect(a.asked).toHaveLength(1);
  });

  it('passes on which lost session it carries on, so the banner can let it go', async () => {
    const d = daemon();

    await resumeConversation(conversation(), { ...d, ...asker(true) }, {}, '0000ffff');

    expect(d.sent[0]).toMatchObject({ agentSession: 'abc-123', agent: 'claude', lost: '0000ffff' });
  });
});

describe('what the page says', () => {
  it('reads the hosted terminal out of the running value', () => {
    expect(hostedIn('hosted:a1b2c3d4')).toBe('a1b2c3d4');
    expect(hostedIn('elsewhere')).toBeNull();
    expect(hostedIn('none')).toBeNull();
  });

  it('says plainly when nothing carries over', () => {
    expect(carriedWords(conversation())).toBe('nothing carried over: no launch on record');
    expect(
      carriedWords(conversation({ launchedFor: { launch: 'x', task: 'fix it', taskId: null, mode: 'implement', profile: null, worktree: null } })),
    ).toBe('carries over task ‘fix it’, mode implement');
  });

  it('names the count and the agents before resuming them all', () => {
    const lost = (agent: string | null): LostSession => ({
      id: 'x',
      endedAt: '',
      agentSession: 'y',
      conversation: agent ? conversation({ agent }) : null,
    });

    expect(resumeAllWords([lost('claude'), lost('claude'), lost('codex'), lost(null)])).toBe(
      'This starts 3 agents: 2 with claude, 1 with codex. Each one carries on its conversation and costs what a session costs.',
    );
  });
});
