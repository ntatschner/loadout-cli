<script lang="ts">
  // One team: what it is and anything that stops it running here, its
  // nodes, what a run of it follows, its runs on this machine, and how its
  // roles have been doing, read only when opened.
  import CommandAction from '../lib/components/CommandAction.svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import RoleRecord from '../lib/components/RoleRecord.svelte';
  import RunCard from '../lib/components/RunCard.svelte';
  import TeamNodes from '../lib/components/TeamNodes.svelte';
  import TeamRules from '../lib/components/TeamRules.svelte';
  import { readJson, SignedOut, teamApi } from '../lib/api';
  import { runJob } from '../lib/jobs';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';
  import type { SheetKind, SheetValues } from '../lib/teamSheet';
  import { summaryOf } from '../lib/teamsData';

  interface Shown {
    name: string;
    description: string;
    goal?: string;
    template: boolean;
    lead: string;
    nodes: { name: string; role: string; mode?: string; deliverable?: string; model?: string; delegates: string[]; worktree: boolean; parallel: number }[];
    rules: {
      autonomy: string;
      budget: { usd?: number; uncapped: boolean; turnsPerNode?: number; wallClock?: string };
      gates: { outward: string; merge: string[]; outwardAllowedWhenAutonomous: string[] };
      stopWhen: string[];
      takeRecommendationAfter?: string;
    };
    doneWhen: string[];
    findings: { kind: string; detail: string }[];
  }

  interface Props {
    name: string;
    /** Opens a sheet, filled in. */
    onsheet?: (kind: SheetKind, preset: Partial<SheetValues>) => void;
  }

  let { name, onsheet = undefined }: Props = $props();

  let team = $state<Shown | null>(null);
  let origin = $state<string | null>(null);
  let problem = $state<string | null>(null);
  let where = $state<string | null>(null);
  let removing = $state(false);

  $effect(() => {
    const asked = name;
    team = null;
    problem = null;
    where = null;

    readJson<Shown>('team show', [asked]).then(
      (read) => (team = read),
      (error: Error) => (error instanceof SignedOut ? (studio.signedOut = true) : (problem = error.message)),
    );
    readJson<{ teams: { name: string; origin: string }[] }>('team list').then(
      (read) => (origin = read.teams.find((one) => one.name === asked)?.origin ?? null),
      () => undefined,
    );
  });

  const yours = $derived(origin === 'workspace' || origin === 'project');
  const runs = $derived(studio.runs.filter((run) => run.team === name));
  const watch = $derived(studio.watchOnly ? 'Studio is only watching: type the command in a terminal' : undefined);

  const rows = $derived<[string, string, string?][]>(
    team
      ? [
          ['How it runs', team.rules.autonomy],
          ['What it may spend', team.rules.budget.uncapped ? 'no cap, on purpose' : team.rules.budget.usd ? `$${team.rules.budget.usd} a run` : 'not set'],
          ...(team.rules.budget.turnsPerNode ? [['Turns a node', String(team.rules.budget.turnsPerNode)] as [string, string]] : []),
          ...(team.rules.budget.wallClock ? [['Time at most', team.rules.budget.wallClock] as [string, string]] : []),
          ['Outside this machine', team.rules.gates.outward],
          ...(team.rules.gates.merge.length ? [['Merges it asks about', team.rules.gates.merge.join(', ')] as [string, string]] : []),
          ['It stops when', team.rules.stopWhen.join('; ') || 'its lead says done'],
          ['Done when', team.doneWhen.length ? team.doneWhen.join('; ') : 'the lead proposes, and you agree before any worker starts'],
          ...(team.rules.takeRecommendationAfter ? [['When nobody answers', `takes the lead’s recommendation after ${team.rules.takeRecommendationAfter}`] as [string, string]] : []),
        ]
      : [],
  );

  async function fileOf() {
    try {
      const job = await runJob('team edit', [name, '--path-only']);
      where = (job.output ?? '').trim();
    } catch (error) {
      problem = (error as Error).message;
    }
  }

  async function remove() {
    const done = await studio.perform(() => runJob('team remove', [name, '--yes']), `${name} is in the bin`, `$ loadout team remove ${name} --yes`);

    if (done) {
      router.go('teams');
    }
  }

  async function roles(): Promise<[string, string | null, number, number, number, number, number][]> {
    const mine = new Set(team?.nodes.map((node) => node.role) ?? []);
    const read = await teamApi.roles();

    return read.roles
      .filter((one) => mine.has(one.role))
      .map((one) => [one.role, one.model, one.runs, one.cost, one.each, Math.round((one.accepting ?? 0) * 100), one.refusals]);
  }
</script>

<a class="back" href="#teams"><span aria-hidden="true">←</span>Teams</a>

{#if problem}<p class="offline" role="alert">{problem}</p>{/if}

{#if !team}
  <p class="calm" role="status">Reading…</p>
{:else}
  <h1 class="topicname">{team.name}</h1>
  <p class="calm">{team.description}</p>
  {#each team.findings as finding, index (index)}
    <p class="ls-note needs">Can’t run here: {finding.detail}</p>
  {/each}

  <div class="ls-answers">
    {#if team.template}
      <HaloButton label="Make a team from it" cmd={`opens Make a team · from ${team.name}`} onpress={() => onsheet?.('make', { from: team!.name })} />
    {:else}
      <HaloButton label="Run it" solid cmd="opens Start a run · starts nothing yet" disabledReason={team.findings.length ? 'can’t run here until that is fixed' : undefined} onpress={() => onsheet?.('run', { team: team!.name })} />
      <HaloButton label="Run it on a schedule" cmd="opens Run one on a schedule" onpress={() => onsheet?.('schedule', { team: team!.name })} />
      <HaloButton label="Make a team from it" cmd={`opens Make a team · from ${team.name}`} onpress={() => onsheet?.('make', { from: team!.name })} />
    {/if}
    <HaloButton label="Where its file is" cmd={`$ loadout team edit ${team.name} --path-only`} onpress={() => void fileOf()} />
    {#if yours}
      <HaloButton label="Remove it" kind="danger" cmd={`$ loadout team remove ${team.name} --yes`} disabledReason={watch} onpress={() => (removing = true)} />
    {/if}
  </div>
  {#if where}<p class="ls-said" role="status">✓ <code class="ls-cmd">{where}</code></p>{/if}

  <section aria-labelledby="tp-nodes">
    <h2 class="ls-zone" id="tp-nodes">Its nodes</h2>
    <TeamNodes nodes={team.nodes.map((node) => ({ name: node.name, role: node.role, mode: node.mode, deliverable: node.deliverable, delegates: node.delegates, instances: node.parallel, worktree: node.worktree }))} />
  </section>

  <section aria-labelledby="tp-rules">
    <h2 class="ls-zone" id="tp-rules">What a run of it follows</h2>
    <TeamRules {rows} />
    <CommandAction path="team capabilities" title="What a team’s declarations can ask for" label="Show" />
  </section>

  <section aria-labelledby="tp-runs">
    <h2 class="ls-zone" id="tp-runs">Its runs here</h2>
    {#if runs.length}
      <div class="runs">
        {#each runs as run (run.id)}<RunCard run={summaryOf(run)} href={`#teams/${encodeURIComponent(run.id)}`} />{/each}
      </div>
    {:else}
      <p class="calm">It has not run on this machine.</p>
    {/if}
  </section>

  <RoleRecord read={roles} />
{/if}

{#if removing && team}
  <Dialog
    danger
    title={`Remove ${team.name}?`}
    why="It goes to the bin, where it waits before it goes for good. Runs it made are not touched."
    actions={[{ label: 'Remove it', kind: 'danger', cmd: `$ loadout team remove ${team.name} --yes`, onpress: () => void remove() }]}
    onclose={() => (removing = false)}
  />
{/if}
