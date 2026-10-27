// Asking Loadout from the palette: '?' turns it into a conversation, Enter
// asks, a near miss can be asked as it stands, and a plan starts nothing
// until Approve, which is held while the cap it needs is blank.
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import CommandPalette from './CommandPalette.svelte';
import { AskConversation } from '../askConversation.svelte';
import { approvePlan, approveReason, isAsk, request, runNowWords, type AskPlan, type AskReply, type PaletteAsk } from '../ask';

const PLAN: AskPlan = {
  shape: 'team-queue',
  team: null,
  teamChoices: ['bug-hunt', 'docs-crew'],
  steps: [
    { kind: 'queue-task', project: 'web', task: 'small', says: 'Queue small on web for the team: Rename a flag', reasons: ['has a done-when, so the finish line is stated'] },
    { kind: 'queue-task', project: 'api', task: 'medium', says: 'Queue medium on api for the team: Add an option', reasons: [] },
    { kind: 'start-team', project: null, task: null, says: 'Start the team on its queue', reasons: [] },
  ],
  capped: true,
  cap: null,
  capNone: false,
  left: ['unbounded on web: no done-when, which a team\'s queue requires'],
  needs: ['which team to use', 'a spending cap: team-budget is not set on this machine'],
};

function reply(query: string, extra: Partial<AskReply> = {}): AskReply {
  return { query, kind: 'answer', answeredBy: 'loadout', summary: `Answer to ${query}`, spend: null, tasks: [], plan: null, nearMisses: [], agentOffer: null, agentCostUsd: null, agentSession: null, ...extra };
}

function setup(
  asker: PaletteAsk['asker'],
  approve: PaletteAsk['approve'] = vi.fn(async () => 'done'),
  agent: PaletteAsk['agent'] = vi.fn(async (query: string) => reply(query, { answeredBy: 'agent' })),
) {
  const conversation = new AskConversation();
  const ask: PaletteAsk = { conversation, asker, approve, agent };
  const onclose = vi.fn();

  render(CommandPalette, { groups: [{ name: 'Go to', items: [{ key: 'home', label: 'Home', sub: 'page' }] }], ask, onclose });

  return { box: screen.getByRole('combobox') as HTMLInputElement, conversation, onclose, approve };
}

describe('asking Loadout from the palette', () => {
  it('reads a leading question mark as a request, and only that', () => {
    expect(isAsk('  ? how much')).toBe(true);
    expect(isAsk('how much?')).toBe(false);
    expect(request('?? how much ')).toBe('how much');
  });

  it('asks on Enter after a question mark, and says who answered', async () => {
    const asker = vi.fn(async (query: string) => reply(query));
    const { box } = setup(asker);

    await fireEvent.input(box, { target: { value: '? how much did I spend' } });
    await fireEvent.keyDown(box, { key: 'Enter' });

    expect(asker).toHaveBeenCalledWith('how much did I spend');
    expect(await screen.findByText('Answer to how much did I spend')).toBeTruthy();
    expect(screen.getByText('Answered by Loadout')).toBeTruthy();
    expect(box.value).toBe('? ');
  });

  it('offers an Ask Loadout row after its own rows, which keeps the palette open and asks', async () => {
    const asker = vi.fn(async (query: string) => reply(query));
    const { box, onclose } = setup(asker);

    await fireEvent.input(box, { target: { value: 'spend last tuesday' } });

    const options = screen.getAllByRole('option').map((option) => option.textContent ?? '');
    expect(options.at(-1)).toContain('Ask Loadout: “spend last tuesday”');

    await fireEvent.click(screen.getByRole('option', { name: /Ask Loadout/ }));

    expect(onclose).not.toHaveBeenCalled();
    expect(asker).toHaveBeenCalledWith('spend last tuesday');
  });

  it('asks a near miss as it stands when it is chosen', async () => {
    const asker = vi.fn(async (query: string) =>
      query === 'who broke the build'
        ? reply(query, { kind: 'unplaced', nearMisses: [{ shape: 'spend', rewrite: 'how much did I spend in the last 7 days across all projects' }] })
        : reply(query));
    const { box } = setup(asker);

    await fireEvent.input(box, { target: { value: '? who broke the build' } });
    await fireEvent.keyDown(box, { key: 'Enter' });
    await fireEvent.click(await screen.findByRole('button', { name: /how much did I spend in the last 7 days/ }));

    expect(asker).toHaveBeenLastCalledWith('how much did I spend in the last 7 days across all projects');
  });

  it('holds Approve while the team and the cap are missing, and starts nothing until it is pressed', async () => {
    const approve = vi.fn(async () => 'Started.');
    const { box } = setup(async (query) => reply(query, { kind: 'plan', plan: PLAN }), approve);

    await fireEvent.input(box, { target: { value: '? queue 2 easy tasks and start it' } });
    await fireEvent.keyDown(box, { key: 'Enter' });

    const button = await screen.findByRole('button', { name: /Approve/ });

    await fireEvent.click(button);
    expect(approve).not.toHaveBeenCalled();
    expect(screen.getByText('Choose a team first.')).toBeTruthy();

    await fireEvent.change(screen.getByRole('combobox', { name: 'Team' }), { target: { value: 'bug-hunt' } });
    expect(await screen.findByText(/Give a spending cap first/)).toBeTruthy();

    await fireEvent.input(screen.getByRole('textbox', { name: /Spending cap/ }), { target: { value: '25' } });
    await fireEvent.click(screen.getByRole('button', { name: /Approve/ }));

    await waitFor(() => expect(approve).toHaveBeenCalledWith(PLAN, 'bug-hunt', '25'));
    expect(await screen.findByText('Started.')).toBeTruthy();
  });

  it('keeps the conversation across closing until it is cleared', async () => {
    const conversation = new AskConversation();

    await conversation.ask('one', async (query) => reply(query));
    await conversation.ask('two', async () => {
      throw new Error('The daemon is not running.');
    });

    expect(conversation.turns.map((turn) => [turn.query, turn.reply?.summary ?? turn.failed])).toEqual([
      ['one', 'Answer to one'],
      ['two', 'The daemon is not running.'],
    ]);

    conversation.clear();
    expect(conversation.turns).toEqual([]);
  });
});

describe('carrying out a plan', () => {
  const runner = () => ({ start: vi.fn(async () => undefined), run: vi.fn(async () => undefined), openTerminal: vi.fn(async () => undefined) });

  it('needs a figure or none for a plan that carries a cap', () => {
    expect(approveReason(PLAN, 'bug-hunt', '')).toMatch(/Give a spending cap/);
    expect(approveReason(PLAN, 'bug-hunt', '0')).toMatch(/above zero/);
    expect(approveReason(PLAN, 'bug-hunt', '$25')).toBeNull();
    expect(approveReason(PLAN, 'bug-hunt', 'none')).toBeNull();
    expect(approveReason({ ...PLAN, capped: false }, 'bug-hunt', '')).toBeNull();
  });

  it('starts a team plan as one run-now job carrying the cap, and waits for nothing', async () => {
    const one = runner();

    const said = await approvePlan(PLAN, 'bug-hunt', '$25', one);

    expect(one.start).toHaveBeenCalledWith('team queue run-now', ['bug-hunt', '--task', 'web/small', '--task', 'api/medium', '--usd', '25']);
    expect(one.run).not.toHaveBeenCalled();
    expect(said).toContain('$25.00');
  });

  it('only queues a plan that does not start the team', async () => {
    const one = runner();
    const queueOnly = { ...PLAN, capped: false, steps: PLAN.steps.filter((step) => step.kind !== 'start-team') };

    await approvePlan(queueOnly, 'bug-hunt', '', one);

    expect(one.start).not.toHaveBeenCalled();
    expect(one.run.mock.calls).toEqual([
      ['team queue add', ['bug-hunt', 'small', '--project', 'web']],
      ['team queue add', ['bug-hunt', 'medium', '--project', 'api']],
    ]);
  });

  it('opens a terminal for each task of a terminals plan', async () => {
    const one = runner();
    const terminals: AskPlan = {
      ...PLAN,
      shape: 'terminals',
      capped: false,
      steps: [{ kind: 'open-terminal', project: 'web', task: 'small', says: '', reasons: [] }, { kind: 'open-terminal', project: 'api', task: 'medium', says: '', reasons: [] }],
    };

    await approvePlan(terminals, null, '', one);

    expect(one.openTerminal.mock.calls).toEqual([['web', 'small'], ['api', 'medium']]);
  });

  it('refuses before doing anything when the plan is not ready', async () => {
    const one = runner();

    await expect(approvePlan(PLAN, null, '25', one)).rejects.toThrow('Choose a team first.');
    expect(one.start).not.toHaveBeenCalled();
    expect(runNowWords(PLAN, 'x', '5')).toEqual(['x', '--task', 'web/small', '--task', 'api/medium', '--usd', '5']);
  });
});

describe('a reply as the command sends it', () => {
  it('fills in what --json left out, so an unset cap is blank rather than undefined', async () => {
    // As 'loadout ask --json' writes it: nulls are left out entirely.
    const { arrived, startingCap } = await import('../ask');
    const wire = {
      query: 'q',
      kind: 'plan',
      answeredBy: 'loadout',
      summary: 's',
      tasks: [],
      plan: { shape: 'team-queue', teamChoices: ['bug-hunt'], steps: [{ kind: 'start-team', says: 'Start', reasons: [] }], capped: true, capNone: false, left: [], needs: [] },
      nearMisses: [],
    };

    const reply = arrived(wire);

    expect(reply.plan!.cap).toBeNull();
    expect(reply.plan!.team).toBeNull();
    expect(reply.plan!.steps[0].task).toBeNull();
    expect(reply.spend).toBeNull();
    expect(startingCap(reply.plan!)).toBe('');
  });
});

describe('the agent, only when chosen', () => {
  const unplaced = (query: string) =>
    reply(query, { kind: 'unplaced', nearMisses: [{ shape: 'tasks', rewrite: 'list 5 open tasks across all projects' }], agentOffer: { estimatedUsd: 0.25, basis: 'a first guess' } });

  it('offers the agent with its rough cost, and asks it nothing until that row is chosen', async () => {
    const agent = vi.fn(async (query: string) => reply(query, { answeredBy: 'agent', agentCostUsd: 0.12, agentSession: 's-1', summary: 'Nobody broke it.' }));
    const { box } = setup(async (query) => unplaced(query), undefined, agent);

    await fireEvent.input(box, { target: { value: '? who broke the build' } });
    await fireEvent.keyDown(box, { key: 'Enter' });

    const row = await screen.findByRole('button', { name: /Ask the agent instead, for about \$0\.25/ });
    expect(agent).not.toHaveBeenCalled();

    await fireEvent.click(row);

    expect(agent).toHaveBeenCalledWith('who broke the build', null);
    expect(await screen.findByText('Nobody broke it.')).toBeTruthy();
    expect(screen.getByText(/Answered by the agent, for \$0\.12/)).toBeTruthy();

    // A second request the agent is chosen for carries its conversation on.
    await fireEvent.input(box, { target: { value: '? and who fixed it' } });
    await fireEvent.keyDown(box, { key: 'Enter' });
    await waitFor(() => expect(screen.getAllByRole('button', { name: /Ask the agent instead/ })).toHaveLength(2));
    await fireEvent.click(screen.getAllByRole('button', { name: /Ask the agent instead/ })[1]);

    expect(agent).toHaveBeenLastCalledWith('and who fixed it', 's-1');
  });

  it("carries the agent's conversation on to the next request, until it is cleared", async () => {
    const conversation = new AskConversation();
    const agent = vi.fn(async (query: string, resume: string | null) => reply(query, { answeredBy: 'agent', agentSession: resume ?? 's-1' }));

    await conversation.ask('first', (query) => agent(query, conversation.agentSession));
    await conversation.ask('second', (query) => agent(query, conversation.agentSession));

    expect(agent.mock.calls).toEqual([['first', null], ['second', 's-1']]);

    conversation.clear();
    expect(conversation.agentSession).toBeNull();
  });

  it('asks the agent as a job, with the session to carry on, and fills in what the reply left out', async () => {
    const { askAgent } = await import('../ask');
    const run = vi.fn(async () => ({ output: JSON.stringify({ query: 'q', kind: 'answer', answeredBy: 'agent', summary: 'done', agentCostUsd: 0.1 }) }) as never);

    const answered = await askAgent('q', 's-9', run);

    expect(run).toHaveBeenCalledWith('ask', ['--use-agent', '--query=q', '--resume', 's-9', '--json'], 300_000);
    expect(answered.agentSession).toBeNull();
    expect(answered.agentCostUsd).toBe(0.1);
  });

  it('offers no agent row when the reply carries no offer', async () => {
    const { box } = setup(async (query) => reply(query, { kind: 'unplaced', nearMisses: [{ shape: 'spend', rewrite: 'how much did I spend in the last 7 days across all projects' }] }));

    await fireEvent.input(box, { target: { value: '? hello' } });
    await fireEvent.keyDown(box, { key: 'Enter' });
    await screen.findByRole('button', { name: /how much did I spend/ });

    expect(screen.queryByRole('button', { name: /Ask the agent/ })).toBeNull();
  });
});
