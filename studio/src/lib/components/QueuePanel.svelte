<script lang="ts" module>
  export interface QueueItemRow {
    position: number;
    task: string;
    project: string;
    after?: string | null;
    by?: string;
    added?: string;
    refused?: string | null;
  }

  export interface QueueRunRow {
    task: string;
    project: string;
    run?: string | null;
    state: string;
    reason?: string | null;
    costUsd: number;
    started: string;
    ended?: string | null;
    by?: string;
  }

  export interface QueueSettingsRow {
    windowUsd?: string | null;
    windowRuns?: number | null;
    heldCap: number;
    draftPullRequests: boolean;
  }

  export interface QueueWindowRow {
    at: string | null;
    lengthMinutes: number;
    autonomy: string;
    open: boolean;
    next?: string | null;
  }

  export interface QueueRead {
    team: string;
    items: QueueItemRow[];
    runs: QueueRunRow[];
    settings: QueueSettingsRow;
    /** When the daemon works through it on its own, or null when only by hand. */
    window?: QueueWindowRow | null;
    /** When the window being worked in opened, while one is. */
    windowOpened?: string | null;
  }

  export interface QueuePlan {
    order: { task: string; project: string }[];
    left: { task: string; project: string; reason: string }[];
    stops: string;
  }

  export interface QueueTask {
    id: string;
    title: string;
    state: string;
    kind: string;
    /** Its finish line; a task without one is refused by the queue. */
    doneWhen?: string[];
  }

  /** A team's name as an owner: builders' and docs-crew's. */
  export const whose = (team: string): string => (team.endsWith('s') ? `${team}'` : `${team}'s`);
</script>

<script lang="ts">
  // One team's queue of fully specified tasks: what waits, in order, and what
  // would be refused now and why; what it has run; and what it is allowed.
  // The draft pull request grant is said first when it is on, because it is
  // the one thing here that reaches outside this machine, and it is turned
  // off from the same line that says so.
  import HaloButton from './HaloButton.svelte';

  interface Props {
    queue: QueueRead;
    projects: string[];
    /** The tasks on a project's list that could be queued. */
    tasksOf: (project: string) => Promise<QueueTask[]>;
    /** Runs one queue command and says whether it was done. */
    act: (path: string, words: string[], done: string) => Promise<boolean | void> | boolean | void;
    /** Reads what the queue would run next. */
    readPlan: () => Promise<QueuePlan>;
    disabledReason?: string;
    /** For a day's spend, so tests can fix the clock. */
    now?: () => number;
  }

  let { queue, projects, tasksOf, act, readPlan, disabledReason = undefined, now = () => Date.now() }: Props = $props();

  const uid = `ls-q-${Math.random().toString(36).slice(2, 7)}`;
  const team = $derived(queue.team);
  const items = $derived(queue.items);
  const settings = $derived(queue.settings);
  const recent = $derived([...queue.runs].reverse().slice(0, 8));
  const held = $derived(queue.runs.filter((run) => run.state === 'held').length);
  const going = $derived(queue.runs.find((run) => run.state === 'running') ?? null);

  // The window being worked in when there is one, since that is what its
  // caps count; otherwise the last day's, which is what a person checking in
  // the morning wants to know.
  const since = $derived(queue.windowOpened ? Date.parse(queue.windowOpened) : now() - 86_400_000);
  const daySpend = $derived(
    queue.runs.filter((run) => Date.parse(run.started) >= since).reduce((sum, run) => sum + run.costUsd, 0),
  );
  const spendWhen = $derived(queue.windowOpened ? 'in this window' : 'in the last day');

  /* ---------- the window ---------- */

  const opening = $derived(queue.window ?? null);
  const span = (minutes: number) =>
    minutes % 60 === 0 ? `${minutes / 60}h` : minutes > 60 ? `${Math.floor(minutes / 60)}h${minutes % 60}m` : `${minutes}m`;

  let timing = $state(false);
  let opensAt = $state('22:00');
  let openFor = $state('8h');

  function editWindow() {
    opensAt = opening?.at ?? '22:00';
    openFor = opening ? span(opening.lengthMinutes) : '8h';
    timing = true;
  }

  const windowWords = $derived([team, '--at', opensAt.trim(), '--for', openFor.trim()]);

  async function saveWindow() {
    if ((await act('team queue window', windowWords, `${whose(team)} queue has its window`)) !== false) {
      timing = false;
    }
  }

  const money = (usd: number) => `$${usd.toFixed(2)}`;
  const at = (iso: string) => new Date(iso).toLocaleString('en-GB', { dateStyle: 'short', timeStyle: 'short' });

  const limits = $derived(
    [
      settings.windowUsd ? `$${settings.windowUsd} a window` : 'no spending cap of its own',
      settings.windowRuns ? `${settings.windowRuns} run(s) a window` : 'no cap on runs',
      `at most ${settings.heldCap} held`,
    ].join(' · '),
  );

  /* ---------- adding ---------- */

  let adding = $state(false);
  let project = $state('');
  let tasks = $state<QueueTask[] | null>(null);
  let task = $state('');
  let after = $state('');

  // Only work that could still be done: an idea has no plan to run from, and
  // a finished task has nothing left. Whether it has a done-when is the
  // command's to say, in its own words, when it is added.
  const offered = $derived((tasks ?? []).filter((one) => one.kind === 'task' && one.state !== 'done' && one.state !== 'dropped'));

  async function pickProject(slug: string) {
    project = slug;
    task = '';
    after = '';
    tasks = null;

    if (slug) {
      tasks = await tasksOf(slug).catch(() => []);
    }
  }

  const addWords = $derived([team, task, '--project', project, ...(after ? ['--after', after] : [])]);

  async function add() {
    if ((await act('team queue add', addWords, `${task} is in ${whose(team)} queue`)) !== false) {
      adding = false;
      task = '';
      after = '';
    }
  }

  /* ---------- the limits ---------- */

  let editing = $state(false);
  let usd = $state('');
  let runs = $state('');
  let heldCap = $state('');

  function edit() {
    usd = settings.windowUsd ?? '';
    runs = settings.windowRuns ? String(settings.windowRuns) : '';
    heldCap = String(settings.heldCap);
    editing = true;
  }

  // Only what changed is sent, so saving one limit never rewrites another
  // somebody changed from a terminal meanwhile.
  const limitWords = $derived.by(() => {
    const words: string[] = [];

    if (usd.trim() !== (settings.windowUsd ?? '')) {
      words.push('--window-usd', usd.trim() || 'none');
    }

    if (runs.trim() !== (settings.windowRuns ? String(settings.windowRuns) : '')) {
      words.push('--window-runs', runs.trim() || 'none');
    }

    if (heldCap.trim() && heldCap.trim() !== String(settings.heldCap)) {
      words.push('--held-cap', heldCap.trim());
    }

    return words;
  });

  async function saveLimits() {
    if ((await act('team queue settings', [team, ...limitWords], `${whose(team)} queue limits are saved`)) !== false) {
      editing = false;
    }
  }

  /* ---------- the plan ---------- */

  let plan = $state<QueuePlan | null>(null);
  let planError = $state<string | null>(null);

  async function showPlan() {
    planError = null;

    try {
      plan = await readPlan();
    } catch (error) {
      plan = null;
      planError = (error as Error).message;
    }
  }
</script>

<section class="ls-queue" aria-label={`${whose(team)} queue`}>
  <header>
    <b>{team}</b>
    <em>{limits}{daySpend > 0 ? ` · ${money(daySpend)} ${spendWhen}` : ''}</em>
  </header>

  <div class="grant" role="group" aria-label={`When ${whose(team)} queue runs`}>
    {#if opening}
      <p>
        Worked through daily from <strong>{opening.at}</strong> for {span(opening.lengthMinutes)}, {opening.autonomy}{opening.open ? ' · open now' : opening.next ? ` · next from ${at(opening.next)}` : ''}.
      </p>
      <HaloButton label="No window" size="small" cmd={`$ loadout team queue window ${team} --off`} {disabledReason} onpress={() => void act('team queue window', [team, '--off'], `${whose(team)} queue has no window`)} />
    {:else}
      <p>No window: its runs start only when you press Run the next one.</p>
    {/if}
    <HaloButton label={timing ? 'Not now' : opening ? 'Change the window' : 'Give it a window'} size="small" cmd={timing ? undefined : `opens ${whose(team)} queue window`} {disabledReason} onpress={() => (timing ? (timing = false) : editWindow())} />
  </div>

  {#if timing}
    <form class="qform" aria-label={`${whose(team)} queue window`} onsubmit={(event) => { event.preventDefault(); void saveWindow(); }}>
      <div class="ls-field">
        <label class="lab" for={`${uid}-at`}>Opens each day at, this machine's time</label>
        <input id={`${uid}-at`} class="ls-text" type="text" autocomplete="off" bind:value={opensAt} placeholder="22:00" />
      </div>
      <div class="ls-field">
        <label class="lab" for={`${uid}-for`}>Stays open for</label>
        <input id={`${uid}-for`} class="ls-text" type="text" autocomplete="off" bind:value={openFor} placeholder="8h" />
      </div>
      <p class="calm small">The daemon has to be running for anything to happen in it.</p>
      <HaloButton label="Save the window" solid cmd={`$ loadout team queue window ${windowWords.join(' ')}`} disabledReason={disabledReason ?? (!opensAt.trim() || !openFor.trim() ? 'Say when it opens and for how long' : undefined)} onpress={() => void saveWindow()} />
    </form>
  {/if}

  {#if settings.draftPullRequests}
    <div class="grant on" role="note">
      <p>It may push a branch and open a <strong>draft pull request</strong> when a queued run meets its done-when. Merging still needs you.</p>
      <HaloButton label="Stop that" size="small" kind="danger" cmd={`$ loadout team queue settings ${team} --draft-prs off`} {disabledReason} onpress={() => void act('team queue settings', [team, '--draft-prs', 'off'], `${whose(team)} queue opens no pull requests`)} />
    </div>
  {:else}
    <div class="grant" role="note">
      <p>It opens no pull requests: a run that meets its done-when keeps its branch on this machine.</p>
      <HaloButton label="Let it open drafts" size="small" cmd={`$ loadout team queue settings ${team} --draft-prs on`} {disabledReason} onpress={() => void act('team queue settings', [team, '--draft-prs', 'on'], `${whose(team)} queue may open draft pull requests`)} />
    </div>
  {/if}

  {#if going || held}
    <p class="ls-note needs">
      {going ? `${going.task} is running.` : ''}{held ? ` ${held} held, waiting for an answer.` : ''}
    </p>
  {/if}

  {#if !items.length}
    <p class="ls-note">Nothing is waiting.</p>
  {:else}
    <ol aria-label={`Waiting in ${whose(team)} queue`}>
      {#each items as item, index (`${item.project}/${item.task}`)}
        <li class:refused={!!item.refused}>
          <div class="t">
            <b>{item.position}. {item.task}</b>
            <span>{item.project}{item.after ? `, after ${item.after}` : ''}</span>
            {#if item.refused}
              <em class="why">Would be refused: {item.refused}</em>
            {/if}
          </div>
          <div class="acts">
            <HaloButton label="Up" size="small" inactive={index === 0} cmd={index === 0 ? 'already first' : `$ loadout team queue move ${team} ${item.task} ${item.position - 1}`} {disabledReason} onpress={() => void act('team queue move', [team, item.task, String(item.position - 1), '--project', item.project], `${item.task} moved up`)} />
            <HaloButton label="Down" size="small" inactive={index === items.length - 1} cmd={index === items.length - 1 ? 'already last' : `$ loadout team queue move ${team} ${item.task} ${item.position + 1}`} {disabledReason} onpress={() => void act('team queue move', [team, item.task, String(item.position + 1), '--project', item.project], `${item.task} moved down`)} />
            <HaloButton label="Take out" size="small" cmd={`$ loadout team queue remove ${team} ${item.task}`} {disabledReason} onpress={() => void act('team queue remove', [team, item.task, '--project', item.project], `${item.task} is out of the queue`)} />
          </div>
        </li>
      {/each}
    </ol>
  {/if}

  <div class="row">
    <HaloButton label="Run the next one" cmd={`$ loadout team queue run-next ${team}`} {disabledReason} onpress={() => void act('team queue run-next', [team], `${whose(team)} queue started its next task`)} />
    <HaloButton label="What would run" cmd={`$ loadout team queue plan ${team}`} onpress={() => void showPlan()} />
    <HaloButton label={adding ? 'Not now' : 'Add a task'} cmd={adding ? undefined : `opens Add a task to ${whose(team)} queue`} {disabledReason} onpress={() => (adding = !adding)} />
    <HaloButton label={editing ? 'Not now' : 'Change its limits'} cmd={editing ? undefined : `opens ${whose(team)} queue limits`} {disabledReason} onpress={() => (editing ? (editing = false) : edit())} />
  </div>

  {#if plan}
    <div class="plan" role="region" aria-label={`What ${whose(team)} queue would run`}>
      {#if plan.order.length}
        <ol>
          {#each plan.order as one (`${one.project}/${one.task}`)}
            <li>{one.task} <span>on {one.project}</span></li>
          {/each}
        </ol>
      {:else}
        <p>Nothing would run.</p>
      {/if}
      {#each plan.left as one (`${one.project}/${one.task}`)}
        <p class="why">Left: {one.task} on {one.project}. {one.reason}</p>
      {/each}
      <!-- The rules' reason for waiting is the left-out reasons joined, said once above. -->
      <p class="calm small">{plan.left.some((one) => plan?.stops.includes(one.reason)) ? '' : `${plan.stops} `}If every run met its done-when.</p>
    </div>
  {:else if planError}
    <p class="ls-note needs">{planError}</p>
  {/if}

  {#if adding}
    <form class="qform" aria-label={`Add a task to ${whose(team)} queue`} onsubmit={(event) => { event.preventDefault(); void add(); }}>
      <div class="ls-field">
        <label class="lab" for={`${uid}-project`}>Project</label>
        <select id={`${uid}-project`} class="ls-text" value={project} onchange={(event) => void pickProject((event.currentTarget as HTMLSelectElement).value)}>
          <option value="">Choose a project</option>
          {#each projects as slug (slug)}
            <option value={slug}>{slug}</option>
          {/each}
        </select>
      </div>
      <div class="ls-field">
        <label class="lab" for={`${uid}-task`}>Task</label>
        <select id={`${uid}-task`} class="ls-text" bind:value={task} disabled={!project || tasks === null}>
          <option value="">{project && tasks === null ? 'Reading…' : 'Choose a task'}</option>
          {#each offered as one (one.id)}
            <option value={one.id}>{one.id} · {one.title}{one.doneWhen && !one.doneWhen.length ? ' (no done-when yet)' : ''}</option>
          {/each}
        </select>
      </div>
      <div class="ls-field">
        <label class="lab" for={`${uid}-after`}>After, if it waits for one</label>
        <select id={`${uid}-after`} class="ls-text" bind:value={after} disabled={!task}>
          <option value="">Nothing, run it in turn</option>
          {#each offered.filter((one) => one.id !== task) as one (one.id)}
            <option value={one.id}>{one.id}</option>
          {/each}
        </select>
      </div>
      <p class="calm small">Only a task with a title and a done-when gets in, and you are told why otherwise.</p>
      <HaloButton label="Add it" solid cmd={`$ loadout team queue add ${addWords.join(' ')}`} disabledReason={disabledReason ?? (!task ? 'Choose a task first' : undefined)} onpress={() => void add()} />
    </form>
  {/if}

  {#if editing}
    <form class="qform" aria-label={`${whose(team)} queue limits`} onsubmit={(event) => { event.preventDefault(); void saveLimits(); }}>
      <div class="ls-field">
        <label class="lab" for={`${uid}-usd`}>Most a window may spend, US dollars</label>
        <input id={`${uid}-usd`} class="ls-text" type="text" autocomplete="off" bind:value={usd} placeholder="none" />
      </div>
      <div class="ls-field">
        <label class="lab" for={`${uid}-runs`}>Most runs a window may start</label>
        <input id={`${uid}-runs`} class="ls-text" type="text" inputmode="numeric" autocomplete="off" bind:value={runs} placeholder="none" />
      </div>
      <div class="ls-field">
        <label class="lab" for={`${uid}-held`}>Runs that may wait at a gate at once</label>
        <input id={`${uid}-held`} class="ls-text" type="text" inputmode="numeric" autocomplete="off" bind:value={heldCap} />
      </div>
      <HaloButton label="Save them" solid cmd={`$ loadout team queue settings ${team} ${limitWords.join(' ')}`.trim()} disabledReason={disabledReason ?? (!limitWords.length ? 'Nothing has changed' : undefined)} onpress={() => void saveLimits()} />
    </form>
  {/if}

  {#if recent.length}
    <h4>What it ran</h4>
    <ul aria-label={`Runs ${whose(team)} queue started`}>
      {#each recent as run (`${run.project}/${run.task}/${run.started}`)}
        <li class={`st-${run.state}`}>
          <div class="t">
            <b>{run.task} <span class="state">{run.state}</span></b>
            <em>{[run.project, at(run.started), run.run, run.costUsd > 0 ? money(run.costUsd) : null].filter(Boolean).join(' · ')}</em>
            {#if run.reason}
              <span>{run.reason}</span>
            {/if}
          </div>
          {#if run.run}
            <a href={`#teams/${encodeURIComponent(run.run)}`}>The run</a>
          {/if}
        </li>
      {/each}
    </ul>
  {/if}
</section>
