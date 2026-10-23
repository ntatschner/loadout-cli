<script lang="ts">
  // Trigger rules: each rule on this machine with where it stands against its
  // caps, a form that writes and changes them, the rules teams suggest, who is
  // trusted beyond the collaborators, and what the rules decided lately.
  // Everything here is read with a 'trigger …' command and done with one; the
  // form shows the line it will run, and Preview runs it with --dry-run first.
  import Dialog from './Dialog.svelte';
  import HaloButton from './HaloButton.svelte';
  import ModePicker from './ModePicker.svelte';
  import Picker from './Picker.svelte';
  import Switch from './Switch.svelte';
  import { readJson } from '../api';
  import { runJob } from '../jobs';
  import { money } from '../teams';
  import { quoted } from '../commands';
  import { ruleCommand, ruleProblem, ruleValues, ruleWords, valuesFromRule, type EventKind, type RuleRead, type RuleValues } from '../triggerRule';

  interface Props {
    /** The teams a rule can start, by name. */
    teams: string[];
    /** The projects a rule can watch, by slug. */
    projects: string[];
    /** Why nothing here can be pressed, when Studio is only watching. */
    disabledReason?: string;
    /** Something went wrong that the page should say. */
    onfail?: (error: unknown) => void;
  }

  let { teams, projects, disabledReason = undefined, onfail = undefined }: Props = $props();

  interface Standing {
    rule: RuleRead;
    problem?: string | null;
    runs: number;
    landedUsd: number;
    reservedUsd: number;
    headroomUsd?: number | null;
    paused: boolean;
    suggestionChanged: boolean;
  }

  interface Suggested {
    team: string;
    name: string;
    rule: string;
    suggested: { on: string; goal?: string; capUsd?: number | null };
    accepted: boolean;
    changed: boolean;
  }

  interface Allowed {
    id: number;
    login: string;
    scope: string;
    nowCalled?: string | null;
  }

  interface Entry {
    at: string;
    kind: string;
    rule?: string | null;
    subject?: string | null;
    reason?: string | null;
    eventKind?: string | null;
  }

  let rules = $state<Standing[] | null>(null);
  let kinds = $state<EventKind[]>([]);
  let builtins = $state<string[]>([]);
  let suggested = $state<Suggested[]>([]);
  let allowed = $state<Allowed[]>([]);
  let journal = $state<Entry[]>([]);

  export async function load() {
    try {
      const [list, events, offered, trusted, recent] = await Promise.all([
        readJson<Standing[]>('trigger list'),
        readJson<{ kinds: EventKind[]; builtins: string[] }>('trigger events'),
        readJson<Suggested[]>('trigger suggested').catch(() => []),
        readJson<Allowed[]>('trigger allowed').catch(() => []),
        readJson<Entry[]>('trigger journal', ['--limit', '25']).catch(() => []),
      ]);

      rules = list;
      kinds = events.kinds;
      builtins = events.builtins;
      suggested = offered;
      allowed = trusted;
      journal = [...recent].reverse();
    } catch (error) {
      onfail?.(error);
    }
  }

  $effect(() => {
    void load();
  });

  /* ---------- the form ---------- */

  let editing = $state<{ values: RuleValues; changing: boolean } | null>(null);
  let said = $state('');
  let refused = $state(false);
  let sending = $state(false);

  const kind = $derived(editing ? (kinds.find((one) => one.kind === editing!.values.on) ?? null) : null);
  const problem = $derived(editing ? ruleProblem(editing.values, kinds, teams) : null);
  const command = $derived(editing ? ruleCommand(editing.values) : '');

  function add() {
    said = '';
    editing = { values: ruleValues({ team: teams[0] ?? '', builtin: builtins[0] ?? 'docs audit' }), changing: false };
  }

  function change(rule: RuleRead) {
    said = '';
    editing = { values: valuesFromRule(rule), changing: true };
  }

  function toggleProject(name: string, on: boolean) {
    if (!editing) {
      return;
    }

    editing.values.projects = on ? [...editing.values.projects, name] : editing.values.projects.filter((one) => one !== name);
  }

  async function send(dry: boolean) {
    if (!editing || sending) {
      return;
    }

    sending = true;

    try {
      const job = await runJob('trigger add', [...ruleWords($state.snapshot(editing.values)), ...(dry ? ['--dry-run'] : [])]);
      said = (job.output ?? '').trim() || (dry ? 'Nothing would change.' : 'Saved.');
      refused = false;

      if (!dry) {
        editing = null;
        void load();
      }
    } catch (error) {
      said = (error as Error).message;
      refused = true;
    } finally {
      sending = false;
    }
  }

  /* ---------- the rules' own buttons ---------- */

  let removing = $state<RuleRead | null>(null);
  let tried = $state<{ rule: string; text: string } | null>(null);

  async function act(path: string, words: string[], after: 'reload' | 'show' = 'reload', rule = '') {
    try {
      const job = await runJob(path, words);

      if (after === 'show') {
        tried = { rule, text: (job.output ?? '').trim() || 'It said nothing.' };
      }
    } catch (error) {
      onfail?.(error);
    }

    void load();
  }

  const line = (path: string, words: string[]) => `loadout ${path} ${words.map(quoted).join(' ')}`;

  function starts(rule: RuleRead) {
    return rule.action.kind === 'team' ? `starts ${rule.action.name} (${rule.action.autonomy})` : `runs ${rule.action.name}`;
  }

  function when(rule: RuleRead) {
    if (rule.on !== 'clock') {
      return `on ${rule.on}`;
    }

    return rule.at ? `daily at ${rule.at.slice(0, 5)}` : `every ${rule.every ?? ''}`;
  }

  function standing(one: Standing) {
    const cap = one.rule.capUsd == null ? '' : ` of ${money(one.rule.capUsd)}`;

    return `${one.runs} of ${one.rule.capRuns} runs · ${money(one.landedUsd)} spent${cap} · ${money(one.reservedUsd)} set aside`;
  }

  /* ---------- the allow-list ---------- */

  let login = $state('');
  let scope = $state<'everywhere' | 'repository' | 'owner'>('everywhere');
  let where = $state('');

  const allowWords = $derived([login.trim() || '<login>', ...(scope === 'repository' ? ['--repository', where.trim()] : scope === 'owner' ? ['--owner', where.trim()] : [])]);
  const allowProblem = $derived(!login.trim() ? 'name the GitHub account first' : scope !== 'everywhere' && !where.trim() ? `say which ${scope}` : null);
</script>

<section class="tblock" aria-labelledby="tr-rules">
  <div class="headrow">
    <h2 class="topicname" id="tr-rules">Trigger rules</h2>
    <HaloButton label="Add a rule" solid glyph="+" {disabledReason} cmd="loadout trigger add …" onpress={add} />
  </div>
  <p class="calm small">A rule starts a team or a built-in when something happens. Each that can spend has a cap, and a run that would write waits for your approval in Needs you.</p>

  {#if rules === null}
    <p class="calm">Reading the rules…</p>
  {:else if !rules.length}
    <p class="calm">There are no rules on this machine yet.</p>
  {:else}
    <ul class="rules">
      {#each rules as one (one.rule.id)}
        <li class="rule" class:off={!one.rule.enabled || one.paused}>
          <div class="top">
            <strong>{one.rule.id}</strong>
            <span class="calm small">v{one.rule.version} · {when(one.rule)} · {starts(one.rule)}</span>
            {#if one.paused}<span class="tag needs">paused at a cap</span>{/if}
            {#if !one.rule.enabled}<span class="tag">disabled</span>{/if}
            {#if one.suggestionChanged}<span class="tag">the team changed its suggestion</span>{/if}
          </div>
          {#if one.rule.action.goal}<p class="goal">{one.rule.action.goal}</p>{/if}
          <p class="calm small">
            {one.rule.projects.length ? `Watches ${one.rule.projects.join(', ')}` : 'Watches every project'}{#each Object.entries(one.rule.where) as [field, value] (field)}{` · ${field} = ${value}`}{/each}
          </p>
          <p class="calm small">{standing(one)}</p>
          {#if one.problem}<p class="prob" role="alert">✕ Not acted on: {one.problem}</p>{/if}
          <div class="acts">
            <HaloButton size="small" label="Change" {disabledReason} cmd={`loadout trigger add ${one.rule.id} …`} onpress={() => change(one.rule)} />
            <HaloButton size="small" label="Try it" cmd={line('trigger test', [one.rule.id])} onpress={() => void act('trigger test', [one.rule.id], 'show', one.rule.id)} />
            {#if one.paused}
              <HaloButton size="small" label="Resume" kind="needs" {disabledReason} cmd={line('trigger resume', [one.rule.id])} onpress={() => void act('trigger resume', [one.rule.id])} />
            {/if}
            <HaloButton size="small" label="Remove" kind="danger" {disabledReason} cmd={line('trigger remove', [one.rule.id])} onpress={() => (removing = one.rule)} />
          </div>
        </li>
      {/each}
    </ul>
  {/if}
</section>

{#if suggested.length}
  <section class="tblock" aria-labelledby="tr-suggested">
    <h2 class="topicname" id="tr-suggested">Suggested by teams</h2>
    <ul class="rules">
      {#each suggested as one (`${one.team}/${one.name}`)}
        <li class="rule">
          <div class="top">
            <strong>{one.team} · {one.name}</strong>
            <span class="calm small">on {one.suggested.on}</span>
            {#if one.accepted && one.changed}<span class="tag needs">changed since you accepted it</span>{:else if one.accepted}<span class="tag">accepted as {one.rule}</span>{/if}
          </div>
          {#if one.suggested.goal}<p class="goal">{one.suggested.goal}</p>{/if}
          {#if !one.accepted || one.changed}
            <div class="acts">
              <HaloButton
                size="small"
                label={one.accepted ? 'Take the change' : 'Accept it'}
                {disabledReason}
                cmd={line('trigger accept', [one.team, one.name])}
                onpress={() => void act('trigger accept', [one.team, one.name])}
              />
            </div>
          {/if}
        </li>
      {/each}
    </ul>
  </section>
{/if}

<section class="tblock" aria-labelledby="tr-trusted">
  <h2 class="topicname" id="tr-trusted">Trusted beyond the collaborators</h2>
  <p class="calm small">Owners, members and collaborators are trusted as GitHub says. Anybody else's words reach a run quoted as theirs, unless they are here.</p>
  {#if allowed.length}
    <ul class="rules">
      {#each allowed as one (`${one.id}:${one.scope}`)}
        <li class="rule">
          <div class="top">
            <strong>{one.login}</strong>
            <span class="calm small">{one.scope}</span>
            {#if one.nowCalled}<span class="tag needs">now called {one.nowCalled}</span>{/if}
          </div>
          <div class="acts">
            <HaloButton size="small" label="Stop trusting" kind="danger" {disabledReason} cmd={line('trigger disallow', [one.login])} onpress={() => void act('trigger disallow', [one.login])} />
          </div>
        </li>
      {/each}
    </ul>
  {/if}
  <div class="allow">
    <div class="ls-field">
      <label class="lab" for="tr-login">GitHub login</label>
      <input id="tr-login" name="tr-login" class="ls-text" type="text" autocomplete="off" placeholder="for example: dependabot[bot]" bind:value={login} />
    </div>
    <ModePicker
      label="Where"
      selected={['everywhere', 'repository', 'owner'].indexOf(scope)}
      options={[{ name: 'Everywhere', desc: 'every repository' }, { name: 'One repository', desc: '--repository owner/name' }, { name: 'One owner', desc: '--owner name' }]}
      onchange={(_, index) => (scope = (['everywhere', 'repository', 'owner'] as const)[index])}
    />
    {#if scope !== 'everywhere'}
      <div class="ls-field">
        <label class="lab" for="tr-where">{scope === 'repository' ? 'The repository, as owner/name' : 'The owner'}</label>
        <input id="tr-where" name="tr-where" class="ls-text" type="text" autocomplete="off" bind:value={where} />
      </div>
    {/if}
    <HaloButton
      label="Trust them"
      disabledReason={allowProblem ?? disabledReason}
      cmd={line('trigger allow', allowWords)}
      onpress={() => {
        void act('trigger allow', allowWords);
        login = '';
        where = '';
      }}
    />
  </div>
</section>

<section class="tblock" aria-labelledby="tr-journal">
  <h2 class="topicname" id="tr-journal">What the rules decided lately</h2>
  {#if journal.length}
    <ul class="journal">
      {#each journal as one, index (index)}
        <li><span class="calm small">{new Date(one.at).toLocaleString()}</span> <strong>{one.kind}</strong>{one.rule ? ` · ${one.rule}` : ''}{one.subject ? ` · ${one.subject}` : ''}{one.reason ? ` · ${one.reason}` : ''}</li>
      {/each}
    </ul>
  {:else}
    <p class="calm">Nothing in the last day.</p>
  {/if}
  <p class="calm small">From <code class="ls-cmd">loadout trigger journal --limit 25</code>; the terminal reads further back with --since.</p>
</section>

{#if editing}
  {@const v = editing.values}
  <Dialog title={editing.changing ? `Change ${v.name}` : 'Add a trigger rule'} label="Trigger rule" extraClass="sheet" cancelLabel="Close" onclose={() => (editing = null)}>
    <div class="ls-sheet">
      <div class="two">
        <div class="ls-field">
          <label class="lab" for="tr-name">Its name</label>
          <input id="tr-name" name="tr-name" class="ls-text" type="text" autocomplete="off" placeholder="for example: review-pulls" readonly={editing.changing} bind:value={editing.values.name} />
          <span class="hint">{editing.changing ? 'Saving keeps the name and counts its version on.' : 'Lowercase words joined by hyphens.'}</span>
        </div>
        <Picker
          label="When this happens"
          options={kinds.map((one) => ({ name: one.kind, sub: `${one.github ? 'GitHub · ' : ''}carries ${one.fields.join(', ')}` }))}
          selected={Math.max(0, kinds.findIndex((one) => one.kind === v.on))}
          onpick={(option) => {
            editing!.values.on = option.name;
            editing!.values.where = editing!.values.where.filter((one) => kinds.find((k) => k.kind === option.name)?.fields.includes(one.field));
          }}
        />
      </div>

      {#if v.on === 'clock'}
        <fieldset>
          <legend>When it comes round</legend>
          <ModePicker label="When" selected={v.clock === 'at' ? 1 : 0} options={[{ name: 'An interval', desc: '--every 1h' }, { name: 'A time of day', desc: '--at 09:00' }]} onchange={(_, index) => (editing!.values.clock = index ? 'at' : 'every')} />
          {#if v.clock === 'at'}
            <div class="ls-field">
              <label class="lab" for="tr-at">The time of day, this machine’s own</label>
              <input id="tr-at" name="tr-at" class="ls-text" type="time" bind:value={editing.values.at} />
            </div>
          {:else}
            <div class="ls-field">
              <label class="lab" for="tr-every">How often</label>
              <input id="tr-every" name="tr-every" class="ls-text" type="text" autocomplete="off" placeholder="1h" bind:value={editing.values.every} />
              <span class="hint">30m, 2h or 1d. At least five minutes.</span>
            </div>
          {/if}
        </fieldset>
      {/if}

      <fieldset>
        <legend>What it starts</legend>
        <ModePicker label="Starts" selected={v.starts === 'builtin' ? 1 : 0} options={[{ name: 'A team', desc: 'a run, with a cap' }, { name: 'A built-in', desc: 'costs nothing' }]} onchange={(_, index) => (editing!.values.starts = index ? 'builtin' : 'team')} />
        {#if v.starts === 'team'}
          <Picker label="Team" options={teams.map((name) => ({ name, sub: '' }))} selected={Math.max(0, teams.indexOf(v.team))} onpick={(option) => (editing!.values.team = option.name)} />
          <div class="ls-field wide">
            <label class="lab" for="tr-goal">What to ask it for</label>
            <textarea id="tr-goal" name="tr-goal" class="ls-text" rows="2" placeholder="for example: Review {'{title}'} and say what would break" bind:value={editing.values.goal}></textarea>
            <span class="hint">{kind ? `{${kind.fields.join('}, {')}} are filled in from the event.` : ''} An outside author’s words are quoted as theirs.</span>
          </div>
          <ModePicker label="Posture" selected={v.autonomy === 'supervised' ? 0 : 1} options={[{ name: 'supervised', desc: 'holds at the gates the team names' }, { name: 'autonomous', desc: 'holds for nothing, within its caps' }]} onchange={(name) => (editing!.values.autonomy = name as RuleValues['autonomy'])} />
        {:else}
          <Picker label="Built-in" options={builtins.map((name) => ({ name, sub: 'reads and reports' }))} selected={Math.max(0, builtins.indexOf(v.builtin))} onpick={(option) => (editing!.values.builtin = option.name)} />
        {/if}
      </fieldset>

      <fieldset>
        <legend>Which events count</legend>
        <p class="hint">{v.projects.length ? '' : 'No project ticked: events from every project count.'}</p>
        <div class="projects">
          {#each projects as name (name)}
            <Switch label={name} checked={v.projects.includes(name)} onchange={(on) => toggleProject(name, on)} />
          {/each}
        </div>
        {#each v.where as condition, index (index)}
          <div class="two">
            <Picker
              label="Field"
              options={(kind?.fields ?? []).map((name) => ({ name, sub: '' }))}
              selected={Math.max(0, (kind?.fields ?? []).indexOf(condition.field))}
              onpick={(option) => (editing!.values.where[index].field = option.name)}
            />
            <div class="ls-field">
              <label class="lab" for={`tr-value-${index}`}>Must be</label>
              <input id={`tr-value-${index}`} name={`tr-value-${index}`} class="ls-text" type="text" autocomplete="off" bind:value={editing.values.where[index].value} />
              <HaloButton size="small" label="Remove this condition" onpress={() => (editing!.values.where = editing!.values.where.filter((_, at) => at !== index))} />
            </div>
          </div>
        {/each}
        {#if kind?.fields.length}
          <HaloButton size="small" label="Add a condition" glyph="+" onpress={() => (editing!.values.where = [...editing!.values.where, { field: kind!.fields[0], value: '' }])} />
        {/if}
        {#if kind?.github}
          <p class="hint">Drafts are passed by unless a condition asks for draft = true, and closed pull requests unless the rule waits for the closing.</p>
        {/if}
      </fieldset>

      <fieldset>
        <legend>What it may spend and how often</legend>
        <div class="two">
          {#if v.starts === 'team'}
            <div class="ls-field">
              <label class="lab" for="tr-cap">Spend cap, in US dollars</label>
              <input id="tr-cap" name="tr-cap" class="ls-text" type="text" autocomplete="off" placeholder="proposed" bind:value={editing.values.capUsd} />
              <span class="hint">Or proposed, worked out from the team’s last runs. Preview shows the figure.</span>
            </div>
            <div class="ls-field">
              <label class="lab" for="tr-budget">Each run may spend</label>
              <input id="tr-budget" name="tr-budget" class="ls-text" type="text" autocomplete="off" placeholder="the team’s own" bind:value={editing.values.budgetUsd} />
              <span class="hint">A figure, or proposed. Needed when the team has no budget of its own.</span>
            </div>
          {/if}
          <div class="ls-field">
            <label class="lab" for="tr-runs">Runs at most in the window</label>
            <input id="tr-runs" name="tr-runs" class="ls-text" type="text" inputmode="numeric" autocomplete="off" bind:value={editing.values.capRuns} />
          </div>
          <div class="ls-field">
            <label class="lab" for="tr-window">The window</label>
            <input id="tr-window" name="tr-window" class="ls-text" type="text" autocomplete="off" bind:value={editing.values.window} />
            <span class="hint">24h or 7d; both caps count over it.</span>
          </div>
          <div class="ls-field">
            <label class="lab" for="tr-debounce">Wait for the subject to go quiet</label>
            <input id="tr-debounce" name="tr-debounce" class="ls-text" type="text" autocomplete="off" placeholder="no wait" bind:value={editing.values.debounce} />
            <span class="hint">10m turns a burst of pushes into one run.</span>
          </div>
        </div>
        <Switch label="Record it without letting it act yet" sub="--disabled" bind:checked={editing.values.disabled} />
      </fieldset>

      <code class="ls-cmdline">{command}</code>
      <div class="foot">
        <HaloButton label="Preview" disabledReason={problem ?? (sending ? 'sending…' : undefined)} cmd={problem ? undefined : 'runs it with --dry-run'} onpress={() => void send(true)} />
        <HaloButton label={editing.changing ? 'Save the change' : 'Save the rule'} solid glyph="▶" disabledReason={problem ?? disabledReason ?? (sending ? 'sending…' : undefined)} cmd={problem ? undefined : 'runs the command above'} onpress={() => void send(false)} />
        <HaloButton label="Close" size="small" onpress={() => (editing = null)} />
      </div>
      {#if said}<pre class={refused ? 'ls-said no output' : 'ls-said output'} role="status">{said}</pre>{/if}
    </div>
  </Dialog>
{/if}

{#if removing}
  {@const rule = removing}
  <Dialog
    danger
    title={`Remove ${rule.id}?`}
    why="It stops acting at once, and anything waiting to start under it, or waiting for your approval, is withdrawn. Its history stays in the journal."
    actions={[{ label: 'Remove it', kind: 'danger', disabledReason, cmd: line('trigger remove', [rule.id]), onpress: () => void act('trigger remove', [rule.id]) }]}
    onclose={() => (removing = null)}
  />
{/if}

{#if tried}
  <Dialog title={`What ${tried.rule} would have done`} label={`Try ${tried.rule}`} cancelLabel="Close" onclose={() => (tried = null)}>
    <pre class="output">{tried.text}</pre>
    <HaloButton label="Close" size="small" onpress={() => (tried = null)} />
  </Dialog>
{/if}

<style>
  .headrow {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: var(--ls-s3, 12px);
    flex-wrap: wrap;
  }

  .rules,
  .journal {
    list-style: none;
    margin: 0;
    padding: 0;
    display: grid;
    gap: var(--ls-s3, 12px);
  }

  .rule {
    border: 1px solid var(--ls-line, currentColor);
    border-radius: var(--ls-r2, 8px);
    padding: var(--ls-s3, 12px);
    max-width: 104ch;
  }

  .rule.off {
    opacity: 0.8;
  }

  .top {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: var(--ls-s2, 8px);
  }

  .goal {
    margin: var(--ls-s1, 4px) 0;
  }

  .tag {
    font-size: 0.85em;
    padding: 0 var(--ls-s2, 8px);
    border: 1px solid var(--ls-line, currentColor);
    border-radius: 999px;
  }

  .tag.needs,
  .prob {
    color: var(--ls-needs, inherit);
  }

  .acts {
    display: flex;
    flex-wrap: wrap;
    gap: var(--ls-s2, 8px);
    margin-top: var(--ls-s2, 8px);
  }

  .allow {
    display: grid;
    gap: var(--ls-s3, 12px);
    max-width: 60ch;
    margin-top: var(--ls-s3, 12px);
  }

  .projects {
    display: flex;
    flex-wrap: wrap;
    gap: var(--ls-s2, 8px);
  }

  .journal li {
    max-width: 96ch;
  }

  .output {
    white-space: pre-wrap;
    max-height: 40vh;
    overflow: auto;
  }
</style>
