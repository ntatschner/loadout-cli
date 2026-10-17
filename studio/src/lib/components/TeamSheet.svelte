<script lang="ts">
  // Start a run, Make a team, or Run one on a schedule, as a sheet that
  // writes its command under the form as you fill it in. Starting a run asks
  // once more, naming the team and the goal, because it spends money and
  // edits a project.
  import { untrack } from 'svelte';
  import Dialog from './Dialog.svelte';
  import HaloButton from './HaloButton.svelte';
  import ModePicker from './ModePicker.svelte';
  import Picker from './Picker.svelte';
  import Switch from './Switch.svelte';
  import { POSTURES, TIMEOUTS, sheetCommand, sheetProblem, sheetValues, type SheetKind, type SheetTeam, type SheetValues } from '../teamSheet';

  interface Props {
    kind: SheetKind;
    teams: SheetTeam[];
    projects: { name: string; path?: string | null }[];
    /** Each project's own done-when, which every run there is held to as well. */
    projectDoneWhen?: Record<string, string[]>;
    preset?: Partial<SheetValues>;
    /** Whether a daemon is running to fire a schedule. */
    daemon?: boolean;
    watchOnly?: boolean;
    /** Runs it; resolves to what to say, or throws with why it was refused. */
    onsubmit?: (kind: SheetKind, values: SheetValues) => Promise<string>;
    onclose?: () => void;
  }

  let { kind, teams, projects, projectDoneWhen = {}, preset = {}, daemon = true, watchOnly = false, onsubmit = undefined, onclose = undefined }: Props = $props();

  const runnable = $derived(teams.filter((team) => !team.template));
  const TITLES: Record<SheetKind, string> = { run: 'Start a run', make: 'Make a team', schedule: 'Run one on a schedule' };

  // Filled once from the preset (Run it again), then the form's own.
  let v = $state<SheetValues>(
    untrack(() =>
      sheetValues({
        team: kind === 'make' ? '' : (teams.find((team) => !team.template)?.name ?? ''),
        project: projects[0]?.name ?? '',
        ...preset,
      }),
    ),
  );

  let confirming = $state(false);
  let said = $state('');
  let refused = $state(false);
  let sending = $state(false);

  const team = $derived(teams.find((one) => one.name === v.team) ?? null);
  const held = $derived(projectDoneWhen[v.project] ?? []);
  const command = $derived(sheetCommand(kind, v));
  const problem = $derived(sheetProblem(kind, v, teams));
  const go = $derived({ run: 'Start the run', make: 'Write it', schedule: 'Schedule it' }[kind]);

  const teamOptions = (list: SheetTeam[]) => list.map((one) => ({ name: one.name, sub: `${one.template ? 'a shape to copy · ' : ''}${one.description}` }));
  const projectOptions = $derived(projects.map((one) => ({ name: one.name, sub: one.path ?? '' })));
  const postureIndex = $derived(Math.max(0, POSTURES.findIndex((one) => one.name === (v.autonomy || team?.posture || 'supervised'))));
  const about = $derived(
    team
      ? [`${team.nodes} nodes`, `led by ${team.lead}`, team.posture, team.budget, team.doneWhen?.length ? `${team.doneWhen.length} done-when of its own` : 'no done-when of its own']
          .filter(Boolean)
          .join(' · ')
      : 'Pick a team to see what it is.',
  );

  function pickProject(name: string) {
    v.project = name;
    // A project's own criteria are its own: leaving one out is decided again for each.
    v.dropped = [];
  }

  function keep(criterion: string, on: boolean) {
    v.dropped = on ? v.dropped.filter((one) => one !== criterion) : [...v.dropped, criterion];
  }

  async function finish() {
    if (sending) {
      return;
    }

    sending = true;

    try {
      said = `✓ ${(await onsubmit?.(kind, $state.snapshot(v))) ?? 'Done.'}`;
      refused = false;
    } catch (error) {
      said = `✕ ${(error as Error).message}`;
      refused = true;
    } finally {
      sending = false;
    }
  }

  function submit() {
    if (kind === 'run') {
      confirming = true;
    } else {
      void finish();
    }
  }
</script>

<Dialog title={TITLES[kind]} label={TITLES[kind]} why={kind === 'run' ? 'A team against a project, with a goal. It asks once more before anything starts.' : undefined} extraClass="sheet" cancelLabel="Close" {onclose}>
  <div class="ls-sheet">
    {#if kind === 'run'}
      <div class="two">
        <Picker label="Team" options={teamOptions(teams)} selected={Math.max(0, teams.findIndex((one) => one.name === v.team))} onpick={(option) => (v.team = option.name)} />
        <Picker label="Project" options={projectOptions} selected={Math.max(0, projects.findIndex((one) => one.name === v.project))} onpick={(option) => pickProject(option.name)} />
      </div>
      <p class="about">{about}{#each team?.problems ?? [] as trouble, index (index)}<span class="prob">✕ Can’t run here: {trouble}</span>{/each}</p>
      <div class="ls-field wide">
        <label class="lab" for="sheet-goal">What the run is for</label>
        <textarea id="sheet-goal" name="sheet-goal" class="ls-text" rows="2" placeholder="for example: make the docs true" bind:value={v.goal}></textarea>
      </div>
      <fieldset>
        <legend>How it runs</legend>
        <ModePicker label="Posture" options={POSTURES} selected={postureIndex} onchange={(name) => (v.autonomy = name === (team?.posture || 'supervised') ? '' : name)} />
      </fieldset>
      <div class="two">
        <div class="ls-field">
          <label class="lab" for="sheet-usd">Budget, in US dollars</label>
          <input id="sheet-usd" name="sheet-usd" class="ls-text" type="text" autocomplete="off" placeholder={team?.budget ? `the team’s ${team.budget}` : 'a figure, or none'} bind:value={v.usd} />
          <span class="hint">Or none, for no cap on purpose.</span>
        </div>
        <div class="ls-field">
          <label class="lab" for="sheet-rounds">Rounds at most</label>
          <input id="sheet-rounds" name="sheet-rounds" class="ls-text" type="text" inputmode="numeric" autocomplete="off" placeholder="no limit" bind:value={v.rounds} />
          <span class="hint">The budget is the cap that binds.</span>
        </div>
      </div>
      <div class="ls-field wide">
        <label class="lab" for="sheet-done">Done when, one per line</label>
        <textarea
          id="sheet-done"
          name="sheet-done"
          class="ls-text"
          rows="3"
          placeholder={team?.doneWhen?.length ? `Empty, it is held to the team’s own:\n${team.doneWhen.join('\n')}` : 'Empty, the lead proposes some and you agree them before any worker starts.'}
          bind:value={v.done}
        ></textarea>
      </div>
      {#if held.length}
        <div class="proj">
          <span class="hint">{v.project} holds every run to these as well. Untick one to leave it out of this run; the journal says so.</span>
          {#each held as criterion (criterion)}
            <Switch label={criterion} sub="the project’s own" checked={!v.dropped.includes(criterion)} onchange={(on) => keep(criterion, on)} />
          {/each}
        </div>
      {/if}
      <fieldset>
        <legend>When nobody answers</legend>
        <div class="two">
          <div class="ls-field">
            <label class="lab" for="sheet-after">Take the lead’s recommendation after</label>
            <input id="sheet-after" name="sheet-after" class="ls-text" type="text" autocomplete="off" placeholder="never" bind:value={v.after} />
            <span class="hint">30m, 2h or 1d. Only the lead’s own questions.</span>
          </div>
          <Picker label="What happens then" options={TIMEOUTS.map((one) => ({ name: one.name, sub: one.sub }))} onpick={(option) => (v.timeout = option.name as SheetValues['timeout'])} />
        </div>
      </fieldset>
    {:else if kind === 'make'}
      <div class="two">
        <div class="ls-field">
          <label class="lab" for="sheet-name">Its name</label>
          <input id="sheet-name" name="sheet-name" class="ls-text" type="text" autocomplete="off" placeholder="for example: quick-review" bind:value={v.name} />
          <span class="hint">Lowercase and hyphenated, as the built-in ones are.</span>
        </div>
        <Picker
          label="Start from"
          options={[{ name: 'An empty team', sub: 'a file with nothing in it yet' }, ...teamOptions(teams)]}
          selected={Math.max(0, ['', ...teams.map((one) => one.name)].indexOf(v.from))}
          onpick={(option, index) => (v.from = index ? option.name : '')}
        />
      </div>
      <Switch label="Only for one project" sub="--for-this-project · otherwise every project sees it" bind:checked={v.forProject} />
      {#if v.forProject}
        <Picker label="Project" options={projectOptions} selected={Math.max(0, projects.findIndex((one) => one.name === v.project))} onpick={(option) => (v.project = option.name)} />
      {/if}
      <p class="ls-note">This writes the file and tells you where it is. Its nodes, rules and done-when are written in the file.</p>
    {:else}
      <div class="two">
        <div class="ls-field">
          <label class="lab" for="sheet-name">A name for the schedule</label>
          <input id="sheet-name" name="sheet-name" class="ls-text" type="text" autocomplete="off" placeholder="for example: nightly-docs" bind:value={v.name} />
        </div>
        <Picker label="Team" options={teamOptions(runnable)} selected={Math.max(0, runnable.findIndex((one) => one.name === v.team))} onpick={(option) => (v.team = option.name)} />
      </div>
      <div class="ls-field wide">
        <label class="lab" for="sheet-goal">What each run is for</label>
        <textarea id="sheet-goal" name="sheet-goal" class="ls-text" rows="2" placeholder="for example: check the docs against the code" bind:value={v.goal}></textarea>
      </div>
      <Picker label="Project" options={projectOptions} selected={Math.max(0, projects.findIndex((one) => one.name === v.project))} onpick={(option) => (v.project = option.name)} />
      <fieldset>
        <legend>When it runs</legend>
        <ModePicker
          label="When"
          selected={['at', 'every', 'on'].indexOf(v.when)}
          options={[{ name: 'A time of day', desc: '--at 09:00' }, { name: 'An interval', desc: '--every 24h' }, { name: 'Something to watch', desc: '--on commit' }]}
          onchange={(_, index) => (v.when = (['at', 'every', 'on'] as const)[index])}
        />
        {#if v.when === 'at'}
          <div class="ls-field">
            <label class="lab" for="sheet-at">The time of day, this machine’s own</label>
            <input id="sheet-at" name="sheet-at" class="ls-text" type="time" bind:value={v.at} />
          </div>
        {:else if v.when === 'every'}
          <div class="ls-field">
            <label class="lab" for="sheet-every">How often</label>
            <input id="sheet-every" name="sheet-every" class="ls-text" type="text" autocomplete="off" placeholder="24h" bind:value={v.every} />
            <span class="hint">30m, 2h or 1d. At least five minutes.</span>
          </div>
        {:else}
          <p class="ls-note">When the project’s repository moves: a new commit since the daemon last looked. The first look only records where it is, and a run’s own commits never fire it again.</p>
        {/if}
      </fieldset>
      <fieldset>
        <legend>How it runs</legend>
        <ModePicker label="Posture" options={POSTURES.slice(1)} selected={v.autonomy === 'autonomous' ? 1 : 0} onchange={(name) => (v.autonomy = name === 'supervised' ? '' : name)} />
        <p class="ls-note">Manual is not offered: it needs a person at every step, and nobody is watching when a schedule fires.</p>
      </fieldset>
      <div class="ls-field">
        <label class="lab" for="sheet-usd">What each run may spend, in US dollars</label>
        <input id="sheet-usd" name="sheet-usd" class="ls-text" type="text" autocomplete="off" placeholder="the team’s own" bind:value={v.usd} />
        <span class="hint">Or none, for no cap.</span>
      </div>
      <p class="ls-note" class:needs={!daemon}>
        {#if daemon}Nothing fires unless the daemon is running. It is running now. A missed run is not made up for later.{:else}The daemon is not running, so nothing will fire. Start it in a terminal: <code class="ls-cmd">$ loadout daemon</code>{/if}
      </p>
    {/if}

    <code class="ls-cmdline">{command}</code>
    <div class="foot">
      <HaloButton
        label={go}
        solid
        glyph="▶"
        disabledReason={problem ?? (watchOnly ? 'Studio is only watching: type the command in a terminal' : sending ? 'sending…' : undefined)}
        cmd={problem ? undefined : 'runs the command above'}
        onpress={submit}
      />
    </div>
    <p class={refused ? 'ls-said no' : 'ls-said'} role="status">{said}</p>
  </div>
</Dialog>

{#if confirming}
  <Dialog
    title={`Start ${v.team} on “${v.goal.trim()}”?`}
    why={`It spends money and edits ${v.project}. Nothing starts until you say so.`}
    actions={[{ label: 'Start it', kind: 'needs', solid: true, cmd: command, onpress: () => void finish() }]}
    onclose={() => (confirming = false)}
  />
{/if}
