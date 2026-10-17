<script lang="ts">
  // Team runs and the teams they run: a run as it stands, with whatever it
  // is waiting on answered where it is shown, its nodes, what it has met,
  // and its journal; and everything that can be done to it, to a team, to a
  // schedule, a remedy or the bin, each through its own command.
  import { untrack } from 'svelte';
  import Command from '../lib/components/Command.svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import Signal from '../lib/components/Signal.svelte';
  import { readJson, SignedOut, type Run } from '../lib/api';
  import { actionsFor } from '../lib/actions';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';

  let { rest }: { rest: string[] } = $props();

  interface Status {
    run: string;
    team: string;
    goal: string;
    project: string | null;
    autonomy: string;
    running: boolean;
    quietSeconds: number;
    cost: number;
    rounds: number;
    roundLimit: number;
    elapsedSeconds: number;
    nodes: { node: string; role: string; state: string; activity: string; turns: number; cost: number; denials: number }[];
    coverage: { criterion: string; verdict: string; verdictInWords: string; because: string }[];
  }

  interface Team {
    name: string;
    description: string;
    template: boolean;
    lead: string;
    nodes: number;
    autonomy: string;
    origin: string;
  }

  interface Shown {
    name: string;
    description: string;
    goal: string;
    nodes: { name: string; role: string; mode: string; deliverable: string; delegates: string[] }[];
    rules: { autonomy: string; budget: { usd: number | null; uncapped: boolean; turnsPerNode: number; wallClock: string }; stopWhen: string[] };
    doneWhen: string[];
  }

  let status = $state<Status | null>(null);
  let log = $state<string[]>([]);
  let teams = $state<Team[] | null>(null);
  let team = $state<Shown | null>(null);
  let schedules = $state<{ name: string; team: string; goal: string; next?: string; every?: string }[] | null>(null);
  let bin = $state<{ keptDays: number; entries: { name: string; kind: string; removed: string; left?: string }[] } | null>(null);
  let wiring = $state<{ notify: { enabled: boolean; where: string; chat: string }; webhook: { enabled: boolean; teams: string[]; listen: string }; attach: { set: boolean; minutes: number } } | null>(null);
  let tools = $state<{ name: string; kind: string; summary: string; lifecycle: string; active?: string; capabilities: string[] }[] | null>(null);
  let toolHealth = $state<{ name?: string; tool?: string; [key: string]: unknown }[] | null>(null);
  let remedyTeam = $state('');
  let remedies = $state<{ name: string; summary?: string; trusted?: boolean }[] | null>(null);
  let problem = $state<string | null>(null);

  // #teams/<run>, #teams/+team/<name>, and #teams/+<place> for the rest.
  const place = $derived(rest[0]?.startsWith('+') ? rest[0].slice(1) : null);
  const runId = $derived(place ? null : (rest[0] ?? null));
  const teamName = $derived(place === 'team' ? (rest[1] ?? null) : null);
  const run = $derived(studio.runs.find((one) => one.id === runId) ?? null);
  const line = $derived(studio.lines.find((one) => one.kind === 'team run' && one.ref === runId) ?? null);

  function fail(error: unknown) {
    if (error instanceof SignedOut) {
      studio.signedOut = true;
    } else {
      problem = (error as Error).message;
    }
  }

  async function loadRun(id: string) {
    try {
      status = await readJson<Status>('team status', [id]);
      const read = await readJson<{ events: { line: string }[] }>('team log', [id]);
      log = read.events.slice(-30).map((event) => event.line);
    } catch (error) {
      fail(error);
    }
  }

  async function loadPlace(where: string | null, name: string | null) {
    try {
      if (where === 'teams' || where === 'start') {
        teams = (await readJson<{ teams: Team[] }>('team list')).teams;
      } else if (where === 'team' && name) {
        team = await readJson<Shown>('team show', [name]);
      } else if (where === 'schedules') {
        schedules = (await readJson<{ schedules: typeof schedules & object }>('team schedule list')).schedules;
      } else if (where === 'tools') {
        tools = (await readJson<{ tools: NonNullable<typeof tools> }>('tools search', ['--all'])).tools;
        readJson<{ tools: NonNullable<typeof toolHealth> }>('tools health').then((read) => (toolHealth = read.tools), () => (toolHealth = null));
      } else if (where === 'bin') {
        bin = await readJson('team bin');
      } else if (where === 'wiring') {
        const [notify, webhook, attach] = await Promise.all([
          readJson<{ enabled: boolean; where: string; chat: string }>('team notify show'),
          readJson<{ enabled: boolean; teams: string[]; listen: string }>('team webhook show'),
          readJson<{ set: boolean; minutes: number }>('team attach show'),
        ]);
        wiring = { notify, webhook, attach };
      }
    } catch (error) {
      fail(error);
    }
  }

  $effect(() => {
    status = null;
    log = [];
    problem = null;

    if (runId) {
      void loadRun(runId);
    }
  });

  $effect(() => {
    team = null;
    problem = null;
    void loadPlace(place, teamName);
  });

  // A job that ended may have changed what is shown, so it is read again in place.
  const settled = $derived(studio.jobs.filter((job) => job.state !== 'running').length);
  let seen = untrack(() => settled);

  $effect(() => {
    if (settled === seen) {
      return;
    }

    seen = settled;

    untrack(() => {
      if (runId) {
        void loadRun(runId);
      }

      void loadPlace(place, teamName);
    });
  });

  // And the run shown, whenever the event stream says it moved on.
  const moved = $derived(run ? `${run.rounds}/${run.running}/${run.gates.length}/${run.nodes.length}` : '');
  let had = untrack(() => moved);

  $effect(() => {
    if (moved === had) {
      return;
    }

    had = moved;

    untrack(() => {
      if (runId) {
        void loadRun(runId);
      }
    });
  });

  async function loadRemedies() {
    if (!remedyTeam.trim()) {
      return;
    }

    try {
      remedies = (await readJson<{ remedies: { name: string; summary?: string; trusted?: boolean }[] }>('team remedies', ['--team', remedyTeam.trim()])).remedies;
    } catch (error) {
      fail(error);
    }
  }

  const word = (one: Run) => (one.waitingForYou ? 'waiting for you' : one.running ? 'running' : one.outcome === 'done' || one.ended === 'done' ? 'done' : one.ended ? (one.ended === 'failed' ? 'failed' : 'stopped') : 'idle');
  const minutes = (seconds: number) => (seconds < 120 ? `${seconds}s` : `${Math.round(seconds / 60)} min`);
  const runWords = (id: string) => ({ RUN: id });

  const PLACES = [
    { key: 'teams', label: 'Teams you can run' },
    { key: 'start', label: 'Start a run' },
    { key: 'schedules', label: 'Runs on a schedule' },
    { key: 'remedies', label: 'Remedies' },
    { key: 'tools', label: 'Shared tools' },
    { key: 'bin', label: 'The bin' },
    { key: 'wiring', label: 'Calls for help and the webhook' },
  ];
</script>

<div class="head"><div><h1>Teams</h1><span class="fact">{studio.runs.filter((one) => one.running).length} running · {studio.runs.filter((one) => one.waitingForYou).length} waiting on you</span></div></div>

<div class="memory">
  <aside class="memside" aria-label="Team runs">
    {#each [{ name: 'Running', ones: studio.runs.filter((one) => one.running) }, { name: 'Ended', ones: studio.runs.filter((one) => !one.running) }] as group (group.name)}
      {#if group.ones.length}
        <section class="store" aria-labelledby={`tm-${group.name}`}>
          <h2 class="ls-zone" id={`tm-${group.name}`}>{group.name}</h2>
          <ul class="slist">
            {#each group.ones as one (one.id)}
              <li>
                <a class="srow" href={`#teams/${encodeURIComponent(one.id)}`} aria-current={runId === one.id ? 'page' : undefined}>
                  <span class="t">{one.goal}</span>
                  <span class="x">{one.team}{one.project ? ` · ${one.project}` : ''} · round {one.rounds}</span>
                  <Signal word={word(one)} />
                </a>
              </li>
            {/each}
          </ul>
        </section>
      {/if}
    {/each}
    {#if !studio.runs.length}<p class="calm">No team runs on this machine yet.</p>{/if}
    <p class="calm small">The same as <code class="ls-cmd">loadout team runs</code>.</p>

    <section class="store" aria-labelledby="tm-more">
      <h2 class="ls-zone" id="tm-more">More</h2>
      <ul class="slist">
        {#each PLACES as one (one.key)}
          <li><a class="srow" href={`#teams/+${one.key}`} aria-current={place === one.key ? 'page' : undefined}><span class="t">{one.label}</span></a></li>
        {/each}
      </ul>
    </section>
  </aside>

  <section class="memmain" aria-label="Team run">
    {#if problem}<p class="offline" role="alert">{problem}</p>{/if}

    {#if runId && run}
      <h2 class="topicname">{run.goal}</h2>
      <div class="jobhead"><Signal word={word(run)} /><span class="calm small">{run.team}{run.project ? ` on ${run.project}` : ''}{status ? ` · ${status.autonomy}` : ''} · round {run.rounds}{run.roundLimit ? ` of ${run.roundLimit}` : ''} · {run.budget.uncapped ? `$${run.budget.spent.toFixed(2)} spent` : `$${run.budget.spent.toFixed(2)} of $${run.budget.cap ?? 0}`}</span></div>

      {#if line?.waiting && line.gate}
        <div class="waitcard" role="group" aria-label="Waiting for you">
          <p class="q">{line.gate.question}</p>
          <span class="w">{line.gate.node} · {line.gate.kind}</span>
          <div class="ls-answers flush">
            {#each actionsFor(studio, line, router.go.bind(router)) as action (action.label)}
              <HaloButton size="small" {...action} />
            {/each}
          </div>
        </div>
      {:else if line}
        <div class="ls-answers flush">
          {#each actionsFor(studio, line, router.go.bind(router)) as action (action.label)}
            <HaloButton size="small" {...action} />
          {/each}
        </div>
      {/if}

      <!-- What the run is waiting on, apart from the question already shown
           above with its answers: that one is 'asking'. -->
      {#each run.attention.filter((item) => !(item.kind === 'asking' && line?.waiting)) as item, index (index)}
        <p class="attention">{item.detail} <span class="calm small">· clears when {item.clears}</span></p>
      {/each}

      {#if status}
        {#if status.coverage.length}
          <h3 class="ls-zone">Done when</h3>
          <ul class="findings">
            {#each status.coverage as one, index (index)}
              <li class={one.verdict === 'met' ? 'info' : 'warning'}><span class="sev">{one.verdictInWords}</span> <span class="n">{one.criterion}</span>{one.because ? ` · ${one.because}` : ''}</li>
            {/each}
          </ul>
        {/if}

        <h3 class="ls-zone">Who is on it</h3>
        <ul class="plain nodes">
          {#each status.nodes as node (node.node)}
            <li><span class="n">{node.node}</span> · {node.role} · {node.activity || node.state} · {node.turns} turn{node.turns === 1 ? '' : 's'} · ${node.cost.toFixed(2)}{node.denials ? ` · ${node.denials} refused` : ''}</li>
          {/each}
        </ul>
        <p class="calm small">{status.running ? `Quiet for ${minutes(status.quietSeconds)}` : 'Ended'} · {minutes(status.elapsedSeconds)} in all. From <code class="ls-cmd">loadout team status {status.run}</code>.</p>
      {/if}

      {#if log.length}
        <h3 class="ls-zone">What happened</h3>
        <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
        <pre class="output" tabindex="0" aria-label="The run's journal">{log.join('\n')}</pre>
        <p class="calm small">The last {log.length} lines of <code class="ls-cmd">loadout team log {run.id}</code>.</p>
      {/if}

      <h3 class="ls-zone">Do</h3>
      <div class="acts">
        {#if run.running}
          <CommandAction path="team message" title="Say something to the lead" preset={runWords(run.id)} fixed={['RUN']} label="Send" />
          <CommandAction path="team say" title="Say something to one node" preset={runWords(run.id)} fixed={['RUN']} label="Send" />
          <CommandAction path="team budget" title="Change what it may spend" preset={runWords(run.id)} fixed={['RUN']} label="Change it" />
          <CommandAction path="team halt" title="Hold it, let it carry on, or stop it" preset={runWords(run.id)} fixed={['RUN']} />
          <CommandAction path="team gate" title="Answer it in your own words" preset={runWords(run.id)} fixed={['RUN']} label="Answer" />
        {:else}
          <CommandAction path="team resume" title="Pick it up where it stopped" preset={runWords(run.id)} fixed={['RUN']} label="Resume" />
          <CommandAction path="team pr" title="Open a pull request for what a node made" preset={runWords(run.id)} fixed={['RUN']} />
          <CommandAction path="team outbox" title="The files it delivered" preset={runWords(run.id)} fixed={['RUN']} label="Show" />
          <CommandAction path="team runs remove" title="Forget it, to the bin" preset={{ run: run.id }} fixed={['run']} label="Forget it" />
        {/if}
        <CommandAction path="team name" title="Name its room" preset={runWords(run.id)} fixed={['RUN']} />
      </div>
    {:else if runId}
      <p class="calm" role="status">Reading…</p>
    {:else if place === 'teams'}
      <h2 class="topicname">Teams you can run</h2>
      {#if teams}
        <ul class="slist">
          {#each teams as one (one.name)}
            <li><a class="srow" href={`#teams/+team/${encodeURIComponent(one.name)}`}><span class="t">{one.name}</span><span class="x">{one.description}</span></a></li>
          {/each}
        </ul>
        <p class="calm small">From <code class="ls-cmd">loadout team list</code>.</p>
      {/if}
      <div class="acts">
        <CommandAction path="team new" title="Write a team of your own" />
        <CommandAction path="team restore" title="Put a team of yours back from the bin" />
        <CommandAction path="team capabilities" title="What a team's declarations can ask for" label="Show" />
        <CommandAction path="team office check" title="Check an office set's scene" label="Check" />
      </div>
    {:else if place === 'team' && team}
      <h2 class="topicname">{team.name}</h2>
      <p class="calm">{team.description}</p>
      <dl class="facts">
        <dt>Autonomy</dt><dd>{team.rules.autonomy}</dd>
        <dt>Budget</dt><dd>{team.rules.budget.uncapped ? 'uncapped' : `$${team.rules.budget.usd}`} · {team.rules.budget.turnsPerNode} turns a node · {team.rules.budget.wallClock}</dd>
        <dt>Stops when</dt><dd>{team.rules.stopWhen.join(', ')}</dd>
        {#if team.doneWhen.length}<dt>Done when</dt><dd>{team.doneWhen.join('; ')}</dd>{/if}
      </dl>
      <h3 class="ls-zone">Who is on it</h3>
      <ul class="plain nodes">
        {#each team.nodes as node (node.name)}<li><span class="n">{node.name}</span> · {node.role} · {node.mode}{node.delegates.length ? ` · briefs ${node.delegates.join(', ')}` : ''}</li>{/each}
      </ul>
      <Command why="From:" command={`$ loadout team show ${team.name}`} />
      <div class="acts">
        <CommandAction path="team run" title="Run it" preset={{ TEAM: team.name }} fixed={['TEAM']} open />
        <CommandAction path="team schedule add" title="Run it on a schedule" preset={{ TEAM: team.name }} fixed={['TEAM']} />
        <CommandAction path="team edit" title="Its file" preset={{ team: team.name, '--path-only': true }} fixed={['team']} label="Show" />
        <CommandAction path="team remove" title="Remove it, to the bin" preset={{ team: team.name, '--yes': true }} fixed={['team']} label="Remove" />
      </div>
    {:else if place === 'start'}
      <h2 class="topicname">Start a run</h2>
      <CommandAction path="team run" title="A team against a project, with a goal" open />
      {#if teams}<p class="calm small">Teams here: {teams.map((one) => one.name).join(', ')}.</p>{/if}
    {:else if place === 'schedules'}
      <h2 class="topicname">Runs on a schedule</h2>
      {#if schedules}
        {#if schedules.length}
          <ul class="plain">
            {#each schedules as one (one.name)}<li><span class="n">{one.name}</span> · {one.team} · {one.goal}{one.next ? ` · next ${new Date(one.next).toLocaleString()}` : ''}</li>{/each}
          </ul>
        {:else}
          <p class="calm">Nothing is scheduled on this machine.</p>
        {/if}
        <p class="calm small">From <code class="ls-cmd">loadout team schedule list</code>. The daemon fires them.</p>
      {/if}
      <div class="acts">
        <CommandAction path="team schedule add" title="Schedule a run" />
        <CommandAction path="team schedule remove" title="Stop one" label="Remove" />
      </div>
    {:else if place === 'remedies'}
      <h2 class="topicname">Remedies</h2>
      <p class="calm">What a team has worked out how to fix, kept as scripts. Trusting one lets it run unattended, so that is done in a terminal.</p>
      <form class="cmdform" onsubmit={(event) => { event.preventDefault(); void loadRemedies(); }}>
        <label class="fld"><span class="lab">The team</span><input name="remedy-team" bind:value={remedyTeam} autocomplete="off" /></label>
        <HaloButton label="Show its remedies" size="small" cmd={`$ loadout team remedies --team ${remedyTeam || '<team>'}`} onpress={() => void loadRemedies()} />
      </form>
      {#if remedies}
        {#if remedies.length}
          <ul class="plain">{#each remedies as one (one.name)}<li><span class="n">{one.name}</span>{one.summary ? ` · ${one.summary}` : ''}{one.trusted ? ' · trusted here' : ''}</li>{/each}</ul>
        {:else}
          <p class="calm">That team has no remedies yet.</p>
        {/if}
      {/if}
      <div class="acts">
        <CommandAction path="team remedy requests" title="Remediations waiting on you" />
        <CommandAction path="team remedy show" title="Read one" label="Show" />
        <CommandAction path="team remedy trust" title="Trust one" />
      </div>
    {:else if place === 'tools'}
      <h2 class="topicname">Shared tools</h2>
      <p class="calm">Scripts every team on this machine can use, each with a manifest, versions and a record of how it has gone. Letting a remediator run one unattended is agreed in a terminal.</p>
      {#if tools}
        {#if tools.length}
          <ul class="plain">
            {#each tools as one (one.name)}
              <li><span class="n">{one.name}</span> · {one.kind} · {one.lifecycle}{one.active ? ` ${one.active}` : ''} · {one.summary}</li>
            {/each}
          </ul>
        {:else}
          <p class="calm">No shared tools yet.</p>
        {/if}
        <p class="calm small">From <code class="ls-cmd">loadout tools search --all</code>{toolHealth ? ' and loadout tools health' : ''}.</p>
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
    {:else if place === 'bin'}
      <h2 class="topicname">The bin</h2>
      {#if bin}
        {#if bin.entries.length}
          <ul class="plain">{#each bin.entries as one (one.name)}<li><span class="n">{one.name}</span> · {one.kind} · removed {new Date(one.removed).toLocaleDateString()}{one.left ? ` · ${one.left} left` : ''}</li>{/each}</ul>
        {:else}
          <p class="calm">The bin is empty.</p>
        {/if}
        <p class="calm small">Kept for {bin.keptDays} days. From <code class="ls-cmd">loadout team bin</code>.</p>
      {/if}
      <div class="acts">
        <CommandAction path="team runs restore" title="Put a run back" label="Put it back" />
        <CommandAction path="team restore" title="Put a team back" label="Put it back" />
        <CommandAction path="team runs prune" title="Forget the old runs" label="Forget them" />
        <CommandAction path="team bin empty" title="Empty it for good" label="Empty it" />
      </div>
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
        <CommandAction path="team attach set" title="Set the passphrase" />
        <CommandAction path="team attach clear" title="Clear the passphrase" />
      </div>
    {:else if studio.runs.length}
      <p class="calm">Choose a run to see where it stands, answer what it is waiting on, and steer it.</p>
    {/if}
  </section>
</div>
