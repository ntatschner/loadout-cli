// The Trigger rules page driven as a person would: the rules and what they
// have spent, a rule written through the form with a preview first, a rule
// removed only once it is confirmed, and an account trusted. Every read and
// every press is a 'trigger …' command, and those are what is checked.
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';

const ran: { path: string; words: string[] }[] = [];

const READS: Record<string, unknown> = {
  'trigger list': [
    {
      rule: {
        id: 'review-pulls', on: 'pr-opened', projects: ['app'], where: { base: 'main' },
        action: { kind: 'team', name: 'reviewer', goal: 'Review {title}', autonomy: 'autonomous' },
        capUsd: 12, capRuns: 10, window: '1.00:00:00', debounce: '00:00:00', enabled: true, version: 3,
      },
      runs: 2, landedUsd: 3.5, reservedUsd: 4, headroomUsd: 4.5, paused: true, suggestionChanged: false,
    },
  ],
  'trigger events': {
    kinds: [
      { kind: 'pr-opened', fields: ['repo', 'base', 'trust', 'draft', 'title'], github: true, ending: false },
      { kind: 'run-finished', fields: ['team', 'project', 'outcome', 'run'], github: false, ending: false },
    ],
    builtins: ['docs audit', 'memory audit'],
  },
  'trigger suggested': [
    { team: 'docs-crew', name: 'on-drift', rule: 'docs-crew-on-drift', suggested: { on: 'docs-drift', goal: 'Fix the docs' }, accepted: false, changed: false },
  ],
  'trigger allowed': [{ id: 49699333, login: 'dependabot[bot]', scope: 'everywhere', nowCalled: null }],
  'trigger journal': [{ at: '2026-10-23T12:00:00Z', kind: 'matched', rule: 'review-pulls', subject: 'pr:acme/app#4' }],
};

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  readJson: vi.fn(async (path: string) => READS[path] ?? []),
}));

vi.mock('../jobs', () => ({
  runJob: vi.fn(async (path: string, words: string[]) => {
    ran.push({ path, words });

    return { output: words.includes('--dry-run') ? 'Dry run: nothing was saved. spend cap: $12.00 -> $20.00' : 'Saved.' };
  }),
}));

const { default: TriggerRules } = await import('./TriggerRules.svelte');

async function settle() {
  for (let i = 0; i < 5; i++) {
    await tick();
    await new Promise((resolve) => setTimeout(resolve, 0));
  }
}

describe('TriggerRules', () => {
  beforeEach(() => {
    ran.length = 0;
  });

  it('lists each rule with what it watches, what it has spent and that it is paused, and offers resume', async () => {
    render(TriggerRules, { teams: ['reviewer'], projects: ['app'] });
    await settle();

    expect(screen.getByText('review-pulls')).toBeTruthy();
    expect(screen.getByText(/Watches app · base = main/)).toBeTruthy();
    expect(screen.getByText('2 of 10 runs · $3.50 spent of $12.00 · $4.00 set aside')).toBeTruthy();
    expect(screen.getByText('paused at a cap')).toBeTruthy();

    await fireEvent.click(screen.getByRole('button', { name: /Resume/ }));
    await settle();

    expect(ran).toContainEqual({ path: 'trigger resume', words: ['review-pulls'] });
  });

  it('changes a rule through the form, previewing with --dry-run before saving it', async () => {
    render(TriggerRules, { teams: ['reviewer'], projects: ['app'] });
    await settle();

    await fireEvent.click(screen.getByRole('button', { name: /^Change/ }));
    await settle();

    const cap = screen.getByLabelText('Spend cap, in US dollars') as HTMLInputElement;
    expect(cap.value).toBe('12');
    await fireEvent.input(cap, { target: { value: '20' } });

    await fireEvent.click(screen.getByRole('button', { name: /^Preview/ }));
    await settle();

    expect(ran.at(-1)?.words).toEqual([
      'review-pulls', 'pr-opened', '--team', 'reviewer', '--goal', 'Review {title}', '--autonomy', 'autonomous',
      '--project', 'app', '--where', 'base=main', '--cap-usd', '20', '--dry-run',
    ]);
    expect(screen.getByRole('status').textContent).toContain('spend cap: $12.00 -> $20.00');

    await fireEvent.click(screen.getByRole('button', { name: /^Save the change/ }));
    await settle();

    expect(ran.at(-1)).toEqual({ path: 'trigger add', words: ran.at(-2)!.words.filter((word) => word !== '--dry-run') });
  });

  it('removes a rule only once the removal is confirmed', async () => {
    render(TriggerRules, { teams: ['reviewer'], projects: ['app'] });
    await settle();

    await fireEvent.click(screen.getByRole('button', { name: /^Remove/ }));
    await settle();
    expect(ran).toEqual([]);

    await fireEvent.click(screen.getByRole('button', { name: /^Remove it/ }));
    await settle();

    expect(ran).toContainEqual({ path: 'trigger remove', words: ['review-pulls'] });
  });

  it('accepts a team’s suggestion and trusts an account on one repository', async () => {
    render(TriggerRules, { teams: ['reviewer'], projects: ['app'] });
    await settle();

    await fireEvent.click(screen.getByRole('button', { name: /^Accept it/ }));
    await settle();
    expect(ran).toContainEqual({ path: 'trigger accept', words: ['docs-crew', 'on-drift'] });

    await fireEvent.input(screen.getByLabelText('GitHub login'), { target: { value: 'renovate[bot]' } });
    await fireEvent.click(screen.getByRole('radio', { name: /One repository/ }));
    await settle();
    await fireEvent.input(screen.getByLabelText('The repository, as owner/name'), { target: { value: 'acme/app' } });
    await fireEvent.click(screen.getByRole('button', { name: /^Trust them/ }));
    await settle();

    expect(ran).toContainEqual({ path: 'trigger allow', words: ['renovate[bot]', '--repository', 'acme/app'] });
  });

  it('greys every change with the reason when Studio is only watching, and still lets a rule be tried', async () => {
    render(TriggerRules, { teams: ['reviewer'], projects: ['app'], disabledReason: 'Studio is only watching' });
    await settle();

    await fireEvent.click(screen.getByRole('button', { name: /^Remove/ }));
    await settle();
    expect(screen.queryByRole('button', { name: /^Remove it/ }), 'no confirmation opens for a press that cannot go').toBeNull();

    await fireEvent.click(screen.getByRole('button', { name: /^Try it/ }));
    await settle();

    expect(ran).toEqual([{ path: 'trigger test', words: ['review-pulls'] }]);
  });
});
