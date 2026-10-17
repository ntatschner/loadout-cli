<script lang="ts">
  // Team runs and the teams they run. The overview holds every reason a run
  // wants you, with its question answered in place, the runs going and
  // ended, the teams you can run and the runs on a schedule; a run, a team,
  // the remedies and the bin each have their own page. Every action says the
  // command it runs. The office stays in the classic dashboard, which is the
  // one that draws rooms.
  import { untrack } from 'svelte';
  import Bin from '../lib/components/Bin.svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import NeedsYou from '../lib/components/NeedsYou.svelte';
  import Picker from '../lib/components/Picker.svelte';
  import Remedy from '../lib/components/Remedy.svelte';
  import RunBoard from '../lib/components/RunBoard.svelte';
  import RunCard from '../lib/components/RunCard.svelte';
  import Schedules from '../lib/components/Schedules.svelte';
  import TeamCard from '../lib/components/TeamCard.svelte';
  import TeamSheet from '../lib/components/TeamSheet.svelte';
  import TeamPage from './TeamPage.svelte';
  import TeamRun from './TeamRun.svelte';
  import { configuration, readJson, runs as runsApi, SignedOut, teamApi, type Choices, type Run } from '../lib/api';
  import { runJob } from '../lib/jobs';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';
  import { sheetPath, sheetWords, type SheetKind, type SheetTeam, type SheetValues } from '../lib/teamSheet';
  import { answerGate, carryOn, forget, hold, pickUp, runLines, stop } from '../lib/teamsAct';
  import { boardOf, boardRunsOf, boardTeamsOf, gateOf, needsOf, summaryOf } from '../lib/teamsData';
  import type { Act, Prune } from '../lib/teams';

  let { rest }: { rest: string[] } = $props();

  interface Team {
    name: string;
    description: string;
    template: boolean;
    lead: string;
    nodes: number;
    autonomy: string;
    origin: string;
  }

  // #teams/<run>, #teams/+team/<name>, and #teams/+<place> for the rest.
  const place = $derived(rest[0]?.startsWith('+') ? rest[0].slice(1) : null);
  const runId = $derived(place ? null : (rest[0] ?? null));
  const teamName = $derived(place === 'team' ? (rest[1] ?? null) : null);

  let teams = $state<Team[] | null>(null);
  let choices = $state<Choices | null>(null);
  let schedules = $state<{ id: string; team: string; goal: string; autonomy: string; every?: string; at?: string; on: string; enabled: boolean; next?: string }[] | null>(null);
  let daemon = $state<{ daemon: boolean; paused: boolean | null } | null>(null);
  let problem = $state<string | null>(null);
  let sheet = $state<{ kind: SheetKind; preset: Partial<SheetValues> } | null>(null);
  let forgetting = $state<Run | null>(null);

  function fail(error: unknown) {
    if (error instanceof SignedOut) {
      studio.signedOut = true;
    } else {
      problem = (error as Error).message;
    }
  }

  async function loadOverview() {
    try {
      const [list, chosen, planned, state] = await Promise.all([
        readJson<{ teams: Team[] }>('team list'),
        teamApi.choices(),
        readJson<{ schedules: NonNullable<typeof schedules> }>('team schedule list'),
        teamApi.daemon(),
      ]);

      teams = list.teams;
      choices = chosen;
      schedules = planned.schedules;
      daemon = state;
    } catch (error) {
      fail(error);
    }
  }

  $effect(() => {
    void place;
    untrack(() => void loadOverview());
  });

  // A job that ended may have changed what is shown, so it is read again in place.
  const settled = $derived(studio.jobs.filter((job) => job.state !== 'running').length);
  let seen = untrack(() => settled);

  $effect(() => {
    if (settled !== seen) {
      seen = settled;
      untrack(() => void loadOverview());
    }
  });

  /* ---------- the overview ---------- */

  const running = $derived(studio.runs.filter((run) => run.running));
  const ended = $derived(studio.runs.filter((run) => !run.running));
  const href = (run: Run) => `#teams/${encodeURIComponent(run.id)}`;
  const watch = $derived(studio.watchOnly ? 'Studio is only watching: type the command in a terminal' : undefined);

  const needs = $derived(
    studio.runs.flatMap((run) =>
      needsOf(run, href(run), (one, gate) => ({ ...gateOf(one, gate), disabledReason: watch, onanswer: (send) => answerGate(one.id, gate.id, send) })),
    ),
  );

  function runningActs(run: Run): Act[] {
    return [
      { label: 'Hold after this round', cmd: runLines.hold(run.id), disabledReason: watch, onpress: () => void hold(run.id) },
      { label: 'Let it carry on', cmd: runLines.carryOn(run.id), disabledReason: watch, onpress: () => void carryOn(run.id) },
      { label: 'Stop after this round', kind: 'danger', cmd: runLines.stop(run.id), disabledReason: watch, onpress: () => void stop(run.id) },
    ];
  }

  function endedActs(run: Run): Act[] {
    return [
      { label: 'Run it again', cmd: 'opens Start a run, filled from this run · starts nothing', onpress: () => again(run) },
      { label: 'Pick it up where it stopped', cmd: runLines.pickUp(run.id), disabledReason: watch, onpress: () => void pickUp(run.id) },
      { label: 'Forget it', kind: 'danger', cmd: runLines.forget(run.id), disabledReason: watch, onpress: () => (forgetting = run) },
    ];
  }

  /** Run it again: the run sheet, filled from the run, which starts nothing until it is sent. */
  function again(run: Run) {
    sheet = {
      kind: 'run',
      preset: {
        team: run.team,
        goal: run.goal,
        project: run.project ?? '',
        autonomy: run.autonomy ?? '',
        usd: run.budget.uncapped ? 'none' : run.budget.cap ? String(run.budget.cap) : '',
        rounds: run.roundLimit ? String(run.roundLimit) : '',
        done: run.coverage.filter((one) => one.source === 'run').map((one) => one.criterion).join('\n'),
      },
    };
  }

  /* ---------- the sheets ---------- */

  const sheetTeams = $derived<SheetTeam[]>(
    (choices?.teams ?? []).map((one) => ({
      name: one.name,
      description: one.description,
      nodes: one.nodes,
      lead: teams?.find((team) => team.name === one.name)?.lead ?? 'its lead',
      posture: one.autonomy,
      doneWhen: one.doneWhen ?? [],
      template: one.template,
      problems: one.trouble ? [one.trouble] : [],
    })),
  );

  const sheetProjects = $derived((choices?.projects ?? []).map((name) => ({ name })));
  const projectDoneWhen = $derived(Object.fromEntries(Object.entries(choices?.projectDoneWhen ?? {}).map(([slug, list]) => [slug, list.map((one) => one.text)])));

  async function submit(kind: SheetKind, v: SheetValues): Promise<string> {
    if (kind === 'run') {
      const done = v.done.split('\n').map((one) => one.trim()).filter(Boolean);
      const answer = await teamApi.start({
        team: v.team,
        goal: v.goal.trim(),
        project: v.project,
        ...(v.autonomy ? { autonomy: v.autonomy } : {}),
        ...(v.rounds ? { rounds: Number(v.rounds) } : {}),
        ...(v.usd ? { budget: v.usd } : {}),
        ...(done.length ? { criteria: done } : {}),
        ...(v.dropped.length ? { dropProjectDoneWhen: v.dropped } : {}),
        ...(v.after ? { takeRecommendationAfter: v.after, onTimeout: v.timeout } : {}),
      });

      if (!answer.started) {
        throw new Error(answer.error ?? 'It did not start.');
      }

      void studio.loadRuns();

      return 'Started. The run is on its page.';
    }

    await runJob(sheetPath(kind), sheetWords(kind, v));
    void loadOverview();

    return kind === 'make' ? `Written. $ loadout team edit ${v.name} --path-only says where.` : daemon?.daemon ? 'Scheduled. The daemon fires it.' : 'Scheduled. It will not fire until the daemon is running.';
  }

  /* ---------- remedies ---------- */

  let remedyTeam = $state('');
  let remedies = $state<{ name: string; kind: string; what: string; trusted: boolean; claimsTrust: boolean; rule: string; ruling: string; revision: number }[] | null>(null);
  let requests = $state<{ id: string; run: string; node: string; role: string; asked: string; at: string }[]>([]);
  let remediationRule = $state<string | null>(null);
  let script = $state<{ name: string; text: string } | null>(null);

  async function loadRemedies(team: string) {
    remedyTeam = team;
    remedies = null;

    try {
      const [read, waiting] = await Promise.all([
        readJson<{ remedies: NonNullable<typeof remedies> }>('team remedies', ['--team', team]),
        readJson<{ waiting: typeof requests }>('team remedy requests', ['--team', team]),
      ]);

      remedies = read.remedies;
      requests = waiting.waiting;
    } catch (error) {
      fail(error);
    }
  }

  $effect(() => {
    if (place !== 'remedies') {
      return;
    }

    untrack(() => {
      configuration().then(
        (groups) => (remediationRule = groups.flatMap((group) => group.settings).find((setting) => setting.name === 'team-remediation')?.value ?? null),
        () => undefined,
      );
    });
  });

  async function readScript(name: string) {
    try {
      const job = await runJob('team remedy show', [name, '--team', remedyTeam, '--script']);
      script = { name, text: job.output ?? '' };
    } catch (error) {
      fail(error);
    }
  }

  const answerRequest = (id: string, approve: boolean, reason: string) =>
    studio.perform(
      () => runJob('team remedy requests', ['--team', remedyTeam, approve ? '--approve' : '--refuse', id, ...(reason ? [`--reason=${reason}`] : [])]),
      approve ? 'Approved once' : 'Refused',
      `$ loadout team remedy requests --team ${remedyTeam} --${approve ? 'approve' : 'refuse'} ${id}`,
      () => loadRemedies(remedyTeam),
    );

  /* ---------- the bin ---------- */

  let bin = $state<{ days: number; entries: { kind: 'run' | 'team'; name: string; team: string; binned: string; goes: string | null; unmerged: string[]; bytes: number }[] } | null>(null);

  $effect(() => {
    if (place === 'bin') {
      untrack(() => void loadBin());
    }
  });

  async function loadBin() {
    try {
      bin = await teamApi.bin();
    } catch (error) {
      fail(error);
    }
  }

  const daysLeft = (goes: string | null) => (goes ? Math.max(0, Math.ceil((Date.parse(goes) - Date.now()) / 86400000)) : 0);
  const sized = (bytes: number) => (bytes < 1048576 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1048576).toFixed(1)} MB`);

  const restore = (entry: { kind: 'run' | 'team'; name: string }) =>
    studio.perform(
      () => runsApi.act(entry.name, entry.kind === 'run' ? 'restore' : 'restore-team'),
      `Put back: ${entry.name}`,
      entry.kind === 'run' ? `$ loadout team runs restore ${entry.name}` : `$ loadout team restore ${entry.name}`,
    );

  const emptyBin = () => studio.perform(() => runJob('team bin empty', ['--yes']), 'The bin is empty', '$ loadout team bin empty --yes', loadBin);

  async function prune(p: Prune, dry: boolean): Promise<string | void> {
    if (dry) {
      const words = [...(p.outcome === 'failed' ? ['--failed'] : p.outcome ? ['--outcome', p.outcome] : []), ...(p.olderThan ? ['--older-than', p.olderThan] : []), ...(p.keep ? ['--keep', p.keep] : []), '--dry-run'];
      const job = await runJob('team runs prune', words).catch((error: Error) => ({ output: error.message }));

      return (job.output ?? '').trim() || 'Nothing would move.';
    }

    await studio.perform(
      async () => {
        const answer = await teamApi.prune({ ...(p.outcome ? { outcome: p.outcome } : {}), ...(p.olderThan ? { olderThan: p.olderThan } : {}), ...(p.keep ? { keep: Number(p.keep) } : {}) });

        if (!answer.cleared) {
          throw new Error(answer.error ?? 'Nothing was cleared out.');
        }
      },
      'Cleared out, to the bin',
      '$ loadout team runs prune',
      loadBin,
    );
  }

  /* ---------- shared tools, and calls for help: as they were ---------- */

  let tools = $state<{ name: string; kind: string; summary: string; lifecycle: string; active?: string; capabilities: string[] }[] | null>(null);
  let wiring = $state<{ notify: { enabled: boolean; where: string; chat: string }; webhook: { enabled: boolean; teams: string[]; listen: string }; attach: { set: boolean; minutes: number } } | null>(null);

  $effect(() => {
    if (place === 'tools') {
      untrack(() => readJson<{ tools: NonNullable<typeof tools> }>('tools search', ['--all']).then((read) => (tools = read.tools), fail));
    } else if (place === 'wiring') {
      untrack(() =>
        Promise.all([
          readJson<{ enabled: boolean; where: string; chat: string }>('team notify show'),
          readJson<{ enabled: boolean; teams: string[]; listen: string }>('team webhook show'),
          readJson<{ set: boolean; minutes: number }>('team attach show'),
        ]).then(([notify, webhook, attach]) => (wiring = { notify, webhook, attach }), fail),
      );
    }
  });

  const PLACES = [
    { key: null, label: 'Runs and teams' },
    { key: 'remedies', label: 'Remedies' },
    { key: 'bin', label: 'The bin' },
    { key: 'tools', label: 'Shared tools' },
    { key: 'wiring', label: 'Calls for help and the webhook' },
  ];

  /* ---------- Team Focus: the board, with a run or a team beside it ---------- */

  const focus = $derived(studio.mode === 'teams' && (!place || place === 'team'));
  const gateFor = (run: Run, gate: Run['gates'][number]) => ({ ...gateOf(run, gate), disabledReason: watch, onanswer: (send: Parameters<typeof answerGate>[2]) => answerGate(run.id, gate.id, send) });
  const board = $derived(boardRunsOf(studio.runs).map((run) => boardOf(run, gateFor)));
  const boardTeams = $derived(boardTeamsOf((teams ?? []).filter((team) => !team.template).map((team) => team.name), studio.runs));
  const liveCount = $derived(running.length);
  const waitingCount = $derived(studio.runs.filter((run) => run.waitingForYou).length);

  const when = (one: NonNullable<typeof schedules>[number]) => (one.at ? `at ${one.at}` : one.on ? `on ${one.on}` : one.every ? `every ${one.every.replace(/^0?(\d+):00:00$/, '$1h').replace(/^00:(\d+):00$/, '$1m')}` : '');
</script>

{#if focus}
  <div class="tfocus" class:open={!!(runId || teamName)}>
    <div class="bcol">
      <div class="head">
        <div>
          <h1>Teams</h1>
          <span class="fact" class:need={waitingCount > 0}>{liveCount} live · {waitingCount ? `${waitingCount} waiting on you` : 'none waiting on you'}</span>
        </div>
        <HaloButton label="Start a run" solid size="small" glyph="▶" cmd="opens the sheet · starts nothing yet" onpress={() => (sheet = { kind: 'run', preset: {} })} />
      </div>
      {#if problem}<p class="offline" role="alert">{problem}</p>{/if}
      <RunBoard
        runs={board}
        teams={boardTeams}
        picked={runId}
        team={teamName}
        onpick={(id) => router.go('teams', id)}
        onteam={(name) => router.go('teams', '+team', name)}
      />
      <p class="calm small">Remedies, the bin, shared tools and the webhook are on the Teams page in Default.</p>
    </div>
    {#if runId || teamName}
      <aside class="bpanel" aria-label={runId ? 'The run' : 'The team'}>
        <div class="pclose"><HaloButton label="Close" size="small" cmd="back to the board" onpress={() => router.go('teams')} /></div>
        {#if runId}
          {#key runId}<TeamRun id={runId} onagain={again} />{/key}
        {:else if teamName}
          {#key teamName}<TeamPage name={teamName} onsheet={(kind, preset) => (sheet = { kind, preset })} />{/key}
        {/if}
      </aside>
    {/if}
  </div>
{:else if runId}
  <TeamRun id={runId} onagain={again} />
{:else if teamName}
  <TeamPage name={teamName} onsheet={(kind, preset) => (sheet = { kind, preset })} />
{:else}
  <div class="head">
    <div>
      <h1>Teams</h1>
      <span class="fact">{running.length} running · {studio.runs.filter((run) => run.waitingForYou).length} waiting on you</span>
    </div>
    <div class="ls-answers flush">
      <HaloButton label="Start a run" solid glyph="▶" cmd="opens Start a run · starts nothing yet" onpress={() => (sheet = { kind: 'run', preset: {} })} />
      <HaloButton label="Make a team" cmd="opens Make a team" onpress={() => (sheet = { kind: 'make', preset: {} })} />
      <HaloButton label="Run one on a schedule" cmd="opens Run one on a schedule" onpress={() => (sheet = { kind: 'schedule', preset: {} })} />
    </div>
  </div>

  <nav class="subnav" aria-label="Teams">
    {#each PLACES as one (one.label)}
      <a href={one.key ? `#teams/+${one.key}` : '#teams'} aria-current={place === one.key ? 'page' : undefined}>{one.label}</a>
    {/each}
  </nav>

  {#if problem}<p class="offline" role="alert">{problem}</p>{/if}

  {#if !place}
    <NeedsYou items={needs} />

    <section class="tblock" aria-labelledby="tm-running">
      <h2 class="ls-zone" id="tm-running">Running</h2>
      {#if running.length}
        <div class="runs">
          {#each running as run (run.id)}<RunCard run={summaryOf(run)} href={href(run)} actions={runningActs(run)} />{/each}
        </div>
      {:else}
        <p class="calm">Nothing is running.</p>
      {/if}
    </section>

    {#if ended.length}
      <section class="tblock" aria-labelledby="tm-ended">
        <h2 class="ls-zone" id="tm-ended">Ended</h2>
        <div class="runs">
          {#each ended as run (run.id)}<RunCard run={summaryOf(run)} href={href(run)} actions={endedActs(run)} />{/each}
        </div>
      </section>
    {/if}

    <section class="tblock" aria-labelledby="tm-teams">
      <h2 class="ls-zone" id="tm-teams">Teams you can run</h2>
      {#if teams}
        <div class="teamcards">
          {#each teams as team (team.name)}
            {@const trouble = choices?.teams.find((one) => one.name === team.name)?.trouble}
            <TeamCard
              name={team.name}
              description={team.description}
              nodes={team.nodes}
              lead={team.lead}
              posture={team.autonomy}
              origin={team.origin === 'builtin' ? 'ships with Loadout' : team.origin === 'pack' ? 'from a pack' : team.origin === 'project' ? 'this project’s' : 'yours'}
              template={team.template}
              problems={trouble ? [trouble] : []}
              doneWhen={choices?.teams.find((one) => one.name === team.name)?.doneWhen?.length ?? 0}
              href={`#teams/+team/${encodeURIComponent(team.name)}`}
              onrun={() => (sheet = { kind: 'run', preset: { team: team.name } })}
              oncopy={() => (sheet = { kind: 'make', preset: { from: team.name } })}
            />
          {/each}
        </div>
      {:else}
        <p class="calm" role="status">Reading…</p>
      {/if}
    </section>

    <section class="tblock" aria-labelledby="tm-sched">
      <h2 class="ls-zone" id="tm-sched">On a schedule</h2>
      {#if schedules && daemon}
        <Schedules
          daemon={{ running: daemon.daemon, paused: daemon.paused }}
          items={schedules.map((one) => ({ name: one.id, team: one.team, goal: one.goal, when: when(one), next: one.next ? new Date(one.next).toLocaleString('en-GB', { dateStyle: 'short', timeStyle: 'short' }) : null, autonomy: one.autonomy, held: one.enabled ? null : 'held' }))}
          disabledReason={watch}
          onremove={(name) => studio.perform(() => runJob('team schedule remove', [name]), `${name} will not fire again`, `$ loadout team schedule remove ${name}`)}
        />
        <p class="calm small">From <code class="ls-cmd">$ loadout team schedule list</code>.</p>
      {:else}
        <p class="calm" role="status">Reading…</p>
      {/if}
    </section>
  {:else if place === 'remedies'}
    <h2 class="topicname">Remedies</h2>
    <p class="calm">What a team has worked out how to fix, kept as scripts. This machine’s rule for running them is <code class="ls-cmd">team-remediation</code>: {remediationRule ?? 'not set'}.</p>
    {#if teams}
      <div class="remteam">
        <Picker label="The team" options={[{ name: 'Pick a team' }, ...teams.filter((one) => !one.template).map((one) => ({ name: one.name, sub: one.description }))]} onpick={(option, index) => index && void loadRemedies(option.name)} />
      </div>
    {/if}
    {#if remedyTeam && !remedies}
      <p class="calm" role="status">Reading…</p>
    {:else if remedies}
      {#if !remedies.length}<p class="calm">{remedyTeam} has no remedies yet.</p>{/if}
      <p class="calm small">From <code class="ls-cmd">$ loadout team remedies --team {remedyTeam}</code> and <code class="ls-cmd">$ loadout team remedy requests --team {remedyTeam}</code>.</p>
      <div class="remedies">
        {#each remedies as one (one.name)}
          <Remedy
            team={remedyTeam}
            name={one.name}
            kind={one.kind}
            what={one.what}
            revision={one.revision}
            rule={one.ruling === 'run' ? 'trusted' : one.ruling === 'refuse' ? 'never' : 'ask'}
            trust={one.trusted ? 'trusted' : one.claimsTrust ? 'changed since trusted' : 'not trusted'}
            requests={requests.filter((request) => request.asked.includes(one.name)).map((request) => ({ id: request.id, node: request.node, run: request.run, problem: request.asked, why: `the rule for ${one.kind} asks first`, asked: new Date(request.at).toLocaleString('en-GB') }))}
            disabledReason={watch}
            onread={() => void readScript(one.name)}
            onanswer={answerRequest}
          />
        {/each}
      </div>
      {@const loose = requests.filter((request) => !remedies!.some((one) => request.asked.includes(one.name)))}
      {#if loose.length}
        <h3 class="ls-zone">Requests waiting on you</h3>
        {#each loose as request (request.id)}
          <section class="ls-gate compact" role="group" aria-label={`Request ${request.id}`}>
            <span class="who">{request.node} · run {request.run} · {request.id}</span>
            <p class="q">{request.asked}</p>
            <div class="ls-answers">
              <HaloButton label="Let it run" kind="needs" glyph="✓" size="small" cmd={`$ loadout team remedy requests --team ${remedyTeam} --approve ${request.id}`} disabledReason={watch} onpress={() => void answerRequest(request.id, true, '')} />
              <HaloButton label="Refuse" glyph="✕" size="small" cmd={`$ loadout team remedy requests --team ${remedyTeam} --refuse ${request.id}`} disabledReason={watch} onpress={() => void answerRequest(request.id, false, '')} />
            </div>
          </section>
        {/each}
      {/if}
    {/if}
  {:else if place === 'bin'}
    <h2 class="topicname">The bin</h2>
    {#if bin}
      <Bin
        keptDays={bin.days}
        entries={bin.entries.map((entry) => ({ kind: entry.kind, name: entry.name, what: entry.kind === 'run' ? entry.team : null, removed: new Date(entry.binned).toLocaleDateString('en-GB'), left: daysLeft(entry.goes), size: sized(entry.bytes) }))}
        disabledReason={watch}
        onrestore={restore}
        onempty={emptyBin}
        onprune={prune}
      />
    {:else}
      <p class="calm" role="status">Reading…</p>
    {/if}
  {:else if place === 'tools'}
    <h2 class="topicname">Shared tools</h2>
    <p class="calm">Scripts every team on this machine can use, each with a manifest, versions and a record of how it has gone. Letting a remediator run one unattended is agreed in a terminal.</p>
    {#if tools}
      {#if tools.length}
        <ul class="plain">
          {#each tools as one (one.name)}<li><span class="n">{one.name}</span> · {one.kind} · {one.lifecycle}{one.active ? ` ${one.active}` : ''} · {one.summary}</li>{/each}
        </ul>
      {:else}
        <p class="calm">No shared tools yet.</p>
      {/if}
      <p class="calm small">From <code class="ls-cmd">loadout tools search --all</code>.</p>
    {:else}
      <p class="calm" role="status">Reading…</p>
    {/if}
    <div class="acts">
      <CommandAction path="tools show" title="One tool: its manifest, versions, usage and trust" label="Show" />
      <CommandAction path="tools health" title="How the active ones are doing" label="Show" />
      <CommandAction path="tools audit" title="The catalogue's log" label="Show" />
      <CommandAction path="tools submit" title="Submit a tool, an idea, a bug or a lesson" label="Submit" />
      <CommandAction path="tools used" title="Record a use of one" label="Record it" />
      <CommandAction path="tools verify" title="Verify a draft against its known-good cases" label="Verify" />
      <CommandAction path="tools promote" title="Promote a verified draft" label="Promote" />
      <CommandAction path="tools deprecate" title="Deprecate one" label="Deprecate" />
      <CommandAction path="tools retire" title="Retire one nobody uses" label="Retire" />
      <CommandAction path="tools trust" title="Let a remediator run one" />
    </div>
    <p class="calm small">Agreeing to a draft's tests, <code class="ls-cmd">--agree</code>, is refused from here and made in a terminal.</p>
  {:else if place === 'wiring'}
    <h2 class="topicname">Calls for help and the webhook</h2>
    {#if wiring}
      <dl class="facts">
        <dt>A run's call for help</dt><dd>{wiring.notify.enabled ? `goes to ${wiring.notify.where}${wiring.notify.chat ? ` (${wiring.notify.chat})` : ''}` : 'goes nowhere'}</dd>
        <dt>Triggered runs</dt><dd>{wiring.webhook.enabled ? `accepted for ${wiring.webhook.teams.join(', ') || 'no team yet'}, on ${wiring.webhook.listen}` : 'not accepted'}</dd>
        <dt>Typing at a running node</dt><dd>{wiring.attach.set ? `allowed for ${wiring.attach.minutes} minutes with the passphrase` : 'needs a passphrase, and none is set'}</dd>
      </dl>
      <p class="calm small">From <code class="ls-cmd">loadout team notify show</code>, <code class="ls-cmd">loadout team webhook show</code> and <code class="ls-cmd">loadout team attach show</code>.</p>
    {/if}
    <div class="acts">
      <CommandAction path="team notify set" title="Send calls for help somewhere" />
      <CommandAction path="team notify test" title="Send a test" label="Send it" />
      <CommandAction path="team notify clear" title="Send them nowhere" label="Clear it" />
      <CommandAction path="team webhook enable" title="Accept triggered runs" label="Enable" />
      <CommandAction path="team webhook disable" title="Stop accepting them" label="Disable" />
    </div>
    <p class="calm small">The passphrase that lets this page type at a node is set and cleared in a terminal, since the page cannot set the passphrase that guards it:</p>
    <code class="ls-cmdline">$ loadout team attach set --passphrase "&lt;something worth having&gt;"</code>
    <code class="ls-cmdline">$ loadout team attach clear</code>
  {/if}
{/if}

{#if sheet}
  {#key sheet}
    <TeamSheet
      kind={sheet.kind}
      preset={sheet.preset}
      teams={sheetTeams}
      projects={sheetProjects}
      {projectDoneWhen}
      daemon={daemon?.daemon ?? true}
      watchOnly={studio.watchOnly}
      onsubmit={submit}
      onclose={() => (sheet = null)}
    />
  {/key}
{/if}

{#if forgetting}
  {@const run = forgetting}
  <Dialog
    danger
    title={`Forget ${run.id}?`}
    why={`“${run.goal}” goes to the bin, where it waits before it goes for good. Its branches are not touched.`}
    actions={[{ label: 'Forget it', kind: 'danger', cmd: runLines.forget(run.id), onpress: () => void forget(run.id) }]}
    onclose={() => (forgetting = null)}
  />
{/if}

{#if script}
  <Dialog title={`${script.name}, as written`} label={script.name} onclose={() => (script = null)} cancelLabel="Close">
    <pre class="output">{script.text}</pre>
  </Dialog>
{/if}
