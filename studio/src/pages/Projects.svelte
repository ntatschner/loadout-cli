<script lang="ts">
  // Projects: a wall of tiles, one per project, with the picked project
  // opened under it as a dossier: its facts, a board of what is being worked
  // on, its health, and what can be done to it, each through its own
  // command. Read with the commands a terminal uses, so the page and
  // 'loadout project show' cannot disagree.
  import { onMount, untrack } from 'svelte';
  import Command from '../lib/components/Command.svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import ForgetDialog from '../lib/components/ForgetDialog.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import HealthList from '../lib/components/HealthList.svelte';
  import ProjectTile from '../lib/components/ProjectTile.svelte';
  import ListToolbar from '../lib/components/ListToolbar.svelte';
  import ConversationList from '../lib/components/ConversationList.svelte';
  import TaskBoard from '../lib/components/TaskBoard.svelte';
  import { CommandList } from '../lib/commandList.svelte';
  import { TaskListView } from '../lib/taskList.svelte';
  import { commands, readJson, SignedOut, type Conversation } from '../lib/api';
  import { lineFor } from '../lib/commandLine';
  import { declareWords, startWords, type Chosen, type Task } from '../lib/tasks';
  import { router } from '../lib/router.svelte';
  import type { Finding } from '../lib/health';
  import { studio } from '../lib/state.svelte';
  import { toasts } from '../lib/toasts.svelte';

  let {
    rest,
    onlaunch,
    onresume = undefined,
  }: { rest: string[]; onlaunch: (chosen?: Chosen) => void; onresume?: (conversation: Conversation) => void } = $props();

  // The history is read again on arrival, for conversations started in a
  // terminal, which the event stream does not hear about.
  onMount(() => void studio.loadHistory().catch(() => undefined));

  interface Listed {
    id: string;
    name: string;
    available: boolean;
    path: string;
    defaultAgent: string;
    pinned: boolean;
  }

  interface Shown {
    id: string;
    name: string;
    remote: string;
    aliases: string[];
    defaultAgent: string;
    available: boolean;
    path: string;
    branch: string | null;
    clean: boolean | null;
    launchCount: number;
    profiles: string[];
    context: Record<string, boolean>;
  }

  interface Drifted {
    project: string;
    findings: (Finding & { name: string; severity: string; detail: string; fixable: boolean })[];
  }

  interface Repository {
    path: string;
    verdict: string;
    compliant: boolean;
    findings: { path?: string; reason?: string; detail?: string }[];
  }

  interface Workspace {
    configured: boolean;
    cloned: boolean;
    remote: string;
    localPath: string;
    projects: number;
  }

  // The wall, through 'project list' with the shared query.
  const wall = new CommandList<Listed>('project list', 'projects', 'projects');
  const projects = $derived(wall.items);
  let shown = $state<Shown | null>(null);
  let worktrees = $state<{ path: string; branch: string; primary: boolean }[] | null>(null);
  let health = $state<Drifted['findings'] | null>(null);
  let repository = $state<Repository | null>(null);
  let workspace = $state<Workspace | null>(null);
  // Each board's query, and what it kept. What left each list is kept apart
  // from its tasks, so no count includes one.
  const projectList = new TaskListView();
  const globalList = new TaskListView();
  let problem = $state<string | null>(null);

  // The tiles' counts, read once the wall is drawn: a tile without them shows none.
  let open = $state<Record<string, { doing: number; blocked: number }>>({});
  let fixable = $state<Record<string, number> | null>(null);
  let ideaCounts = $state<Record<string, number> | null>(null);

  const slug = $derived(rest[0] && !rest[0].startsWith('+') ? rest[0] : null);
  const place = $derived(rest[0]?.startsWith('+') ? rest[0].slice(1) : null);
  const refused = $derived(studio.watchOnly ? studio.watchOnlyReason : undefined);

  function fail(error: unknown) {
    if (error instanceof SignedOut) {
      studio.signedOut = true;
    } else {
      problem = (error as Error).message;
    }
  }

  async function loadList() {
    await untrack(() => wall.load([]));

    if (wall.items) {
      loadCounts(wall.items);
    }
  }

  function loadCounts(all: Listed[]) {
    for (const one of all) {
      readJson<{ tasks: Task[] }>('task list', ['--project', one.id]).then(
        (read) => (open = { ...open, [one.id]: { doing: read.tasks.filter((task) => task.state === 'doing').length, blocked: read.tasks.filter((task) => task.state === 'blocked').length } }),
        () => undefined,
      );
    }

    readJson<Drifted[]>('drift').then(
      (read) => (fixable = Object.fromEntries(read.map((one) => [one.project, one.findings.filter((finding) => finding.fixable).length]))),
      () => (fixable = null),
    );
    readJson<{ ideas: { project?: string | null; stage: string }[] }>('idea list').then((read) => {
      const by: Record<string, number> = {};

      for (const idea of read.ideas.filter((one) => one.stage !== 'accepted')) {
        if (idea.project) {
          by[idea.project] = (by[idea.project] ?? 0) + 1;
        }
      }

      ideaCounts = by;
    }, () => (ideaCounts = null));
  }

  async function loadProject(name: string) {
    try {
      shown = await readJson<Shown>('project show', [name]);
    } catch (error) {
      fail(error);

      return;
    }

    // The rest is worth having but not worth holding the facts up for.
    readJson<{ worktrees: NonNullable<typeof worktrees> }>('project worktrees', [name]).then((read) => (worktrees = read.worktrees), () => (worktrees = null));
    readJson<Drifted[]>('drift', [name]).then((read) => (health = read[0]?.findings ?? []), () => (health = null));
    readJson<{ repositories: Repository[] }>('repo check', [name]).then((read) => (repository = read.repositories[0] ?? null), () => (repository = null));
    loadTasks(name);
  }

  // Read outside any effect's tracking: the query is read inside, and a
  // change to it reads the list itself.
  function loadTasks(name: string) {
    untrack(() => void projectList.load(['--project', name]));
  }

  function loadGlobal() {
    untrack(() => void globalList.load(['--global']));
  }

  $effect(() => {
    void loadList();
  });

  $effect(() => {
    shown = null;
    health = null;
    repository = null;
    worktrees = null;
    projectList.clear();
    problem = null;

    if (slug) {
      void loadProject(slug);
    }
  });

  $effect(() => {
    if (place === 'tasks') {
      loadGlobal();
    } else if (place === 'workspace') {
      readJson<Workspace>('workspace status').then((read) => (workspace = read), fail);
    }
  });

  // A job that ended may have changed what is shown, so it is read again in
  // place, without clearing the page first.
  const settled = $derived(studio.jobs.filter((job) => job.state !== 'running').length);
  let seen = untrack(() => settled);

  $effect(() => {
    if (settled === seen) {
      return;
    }

    seen = settled;

    untrack(() => {
      void loadList();

      if (slug) {
        void loadProject(slug);
      }

      if (place === 'tasks') {
        loadGlobal();
      }
    });
  });

  /** Runs one step as a job; the page reads again when it ends. */
  async function act(path: string, words: string[]): Promise<boolean> {
    if (studio.watchOnly) {
      toasts.add({ level: 'info', text: 'Studio is only watching, so nothing was changed.', cmd: lineFor(path, words) });

      return false;
    }

    try {
      await commands.run(path, words);
      await studio.loadJobs();

      return true;
    } catch (error) {
      fail(error);

      return false;
    }
  }

  /** Starts a session on a task, at once or through the launch sheet. */
  async function start(task: Task, withOptions: boolean) {
    const project = shown!.id;

    if (withOptions) {
      onlaunch({ project, id: task.id, title: task.title, state: task.state });

      return;
    }

    // The launch moves open or blocked work to doing itself, as it does
    // from a terminal, so nothing is declared from here.
    const started = await studio.startSession({ project, taskId: task.id }, lineFor('launch', startWords(project, task.id)));

    if (started) {
      router.go('session', started.id);
    }
  }

  // Record a task.
  let newTitle = $state('');
  let newId = $state('');
  const recordWords = $derived(
    shown ? [...declareWords(newId.trim() || '<id>', 'open', []), '--title', newTitle.trim() || '<title>', '--project', shown.id] : [],
  );

  async function record() {
    if (await act('task declare', recordWords)) {
      newTitle = '';
      newId = '';
    }
  }

  // Forget project, from the dossier.
  let forgetting = $state(false);

  const PLACES = [
    { key: 'add', label: 'Add a repository you have', sub: 'loadout project add' },
    { key: 'new', label: 'Make a new project', sub: 'loadout project new' },
    { key: 'find', label: 'Find repositories and stray memory', sub: 'loadout project discover' },
  ];

  const picked = $derived(!!slug || !!place);
  const fixes = $derived(health?.filter((finding) => finding.fixable).length ?? 0);
  const repoFinding = $derived<Finding | null>(
    repository
      ? { name: 'AI tooling files', severity: repository.compliant ? 'Info' : 'Warning', detail: repository.compliant ? 'none tracked in the repository' : `${repository.findings.length} tracked in the repository` }
      : null,
  );
  const extras = $derived(shown ? Object.entries(shown.context).filter(([, on]) => on).map(([key]) => key) : []);
</script>

<div class="head"><div><h1>Projects</h1><span class="fact">{projects ? (wall.recorded !== null && wall.recorded !== projects.length ? `${wall.recorded} known · ${projects.length} shown` : `${projects.length} known · ${projects.filter((one) => one.available).length} on this machine`) : 'the projects Loadout knows'}</span></div></div>

{#if problem}<p class="offline" role="alert">{problem}</p>{/if}
{#if wall.error}<p class="offline" role="alert">{wall.error}</p>{/if}

<ListToolbar noun="projects" slash={!slug && place !== 'tasks'} query={wall.text} shape={wall.shape} shown={projects?.length ?? null} recorded={wall.recorded} problem={wall.problem} onchange={(query) => void wall.ask(query).then(() => wall.items && loadCounts(wall.items))} />

<nav aria-label="Projects">
  <div class={['ls-pwall', picked ? 'compact' : ''].filter(Boolean).join(' ')}>
    {#if projects === null}
      <p class="calm" role="status">Reading…</p>
    {/if}
    {#each projects ?? [] as one (one.id)}
      <ProjectTile project={one} href={`#projects/${encodeURIComponent(one.id)}`} picked={slug === one.id}
        doing={open[one.id]?.doing} blocked={open[one.id]?.blocked} findings={fixable ? (fixable[one.id] ?? 0) : undefined} ideas={ideaCounts?.[one.id]} />
    {/each}
    {#each PLACES as one (one.key)}
      <ProjectTile place={one.label} sub={one.sub} href={`#projects/+${one.key}`} picked={place === one.key} />
    {/each}
  </div>
  <p class="ls-from">The same as <code>$ loadout project list</code> or <code>loadout list</code>. Also <a href="#projects/+tasks" aria-current={place === 'tasks' ? 'page' : undefined}>tasks for no project yet</a> and <a href="#projects/+workspace" aria-current={place === 'workspace' ? 'page' : undefined}>the workspace</a>.</p>
</nav>

{#if slug && shown}
  <section class="dossier" aria-labelledby="dh">
    <div class="dhead"><h2 id="dh">{shown.name}</h2><span class="slug">{shown.id}{shown.aliases.length ? ` · also ${shown.aliases.join(', ')}` : ''}</span></div>
    <dl class="facts">
      <div><dt>Where</dt><dd>{#if shown.available}<code>{shown.path}</code>{:else}not on this machine{/if}</dd></div>
      <div><dt>Branch</dt><dd>{shown.branch ?? 'none'}{shown.clean === false ? ' · uncommitted changes' : shown.clean ? ' · clean' : ''}</dd></div>
      <div><dt>Remote</dt><dd>{shown.remote || 'none recorded'}</dd></div>
      <div><dt>Agent</dt><dd>{shown.defaultAgent}</dd></div>
      <div><dt>Launched</dt><dd>{shown.launchCount} time{shown.launchCount === 1 ? '' : 's'}</dd></div>
      <div><dt>Profiles</dt><dd>{shown.profiles.length ? shown.profiles.join(', ') : 'none'}</dd></div>
      <div><dt>Every session also gets</dt><dd>{extras.join(', ') || 'nothing extra'}</dd></div>
      <div class={worktrees && worktrees.length > 1 ? 'wide' : ''}><dt>Working trees</dt><dd>{#if worktrees === null}…{:else if worktrees.length <= 1}just the one{:else}<ul class="trees">{#each worktrees as tree (tree.path)}<li><code>{tree.path}</code> · {tree.branch}{tree.primary ? ' · the main one' : ''}</li>{/each}</ul>{/if}</dd></div>
    </dl>
    <p class="ls-from">From <code>$ loadout project show {shown.id}</code> and <code>$ loadout project worktrees {shown.id}</code>.</p>
    <Command why="The same, in a terminal:" command={lineFor('project show', [shown.id])} />

    <div class="dgrid">
      <section class="pblock" aria-labelledby="dt">
        <h2 class="ls-zone" id="dt">What is being worked on</h2>
        <ListToolbar noun="tasks" query={projectList.text} shape={projectList.shape} shown={projectList.tasks?.length ?? null} recorded={projectList.recorded} problem={projectList.problem} onchange={(query) => void projectList.ask(query)} />
        {#if projectList.tasks}
          <TaskBoard tasks={projectList.tasks} away={projectList.away} project={shown.id} onstart={(task, withOptions) => void start(task, withOptions)} where={['--project', shown.id]} from={projectList.from} disabledReason={refused} ondeclare={(task, state) => void act('task declare', declareWords(task.id, state, ['--project', shown!.id]))} />
        {:else}
          <p class="calm" role="status">Reading…</p>
        {/if}
        <form class="tasknew" onsubmit={(event) => { event.preventDefault(); if (newTitle.trim() && newId.trim()) void record(); }}>
          <div class="ls-field"><label class="lab" for="tn-title">Record a task</label><input id="tn-title" class="ls-text" placeholder="What needs doing" bind:value={newTitle} autocomplete="off" /></div>
          <div class="ls-field"><label class="lab" for="tn-id">Its id</label><input id="tn-id" class="ls-text" placeholder="a-short-id" bind:value={newId} autocomplete="off" spellcheck="false" /></div>
          <HaloButton label="Record it" size="small" solid={!refused && !!newTitle.trim() && !!newId.trim()} cmd={lineFor('task declare', recordWords)} disabledReason={refused ?? (!newTitle.trim() ? 'say what needs doing' : !newId.trim() ? 'give it an id' : undefined)} onpress={() => void record()} />
        </form>
      </section>

      <section class="pblock" aria-labelledby="dhl">
        <h2 class="ls-zone" id="dhl">Health</h2>
        {#if health === null}
          <p class="calm" role="status">Checking…</p>
        {:else}
          <HealthList findings={repoFinding ? [...health, repoFinding] : health} from={`$ loadout drift ${shown.id} and loadout repo check ${shown.id}`} />
          {#if fixes}
            <CommandAction path="drift" title={`Put right the ${fixes} that can be put right`} preset={{ PROJECT: shown.id, '--fix': true, '--yes': true }} fixed={['PROJECT']} label="Put it right" />
          {/if}
        {/if}
      </section>

      <section class="pblock" aria-labelledby="drc">
        <h2 class="ls-zone" id="drc">Recent conversations</h2>
        <!-- This project's, from the history Sessions keeps; the rest are there. -->
        <ConversationList conversations={studio.conversations.filter((one) => one.project === shown!.id).slice(0, 5)} label={`Recent conversations in ${shown.id}`} onchanges={onresume} inProject />
      </section>
    </div>

    <section class="pblock" aria-labelledby="dd">
      <h2 class="ls-zone" id="dd">Do</h2>
      <div class="ls-drawers">
        <h4 class="gh">Open it</h4>
        {#if shown.available}
          <CommandAction path="code" title="Open it in the editor" preset={{ project: shown.id }} fixed={['project']} label="Open" />
          <CommandAction path="project open" title="Open its folder" preset={{ project: shown.id }} fixed={['project']} label="Open" />
        {:else}
          <CommandAction path="project clone" title="Clone it onto this machine" preset={{ project: shown.id }} fixed={['project']} label="Clone it" />
        {/if}
        <CommandAction path="project onboard" title="Onboard it with an agent" preset={{ project: shown.id }} {onlaunch} />
        <h4 class="gh">Change how it is set up</h4>
        <CommandAction path="project context" title="Change what every session also gets" preset={{ '--project': shown.id }} fixed={['--project']} />
        <CommandAction path="project proposal" title="A proposed change to its settings" preset={{ project: shown.id }} fixed={['project']} />
        <CommandAction path="project relocate" title="It has moved on this machine" preset={{ project: shown.id }} fixed={['project']} />
        <CommandAction path="project link" title="Record in the repository which project it is" preset={{ project: shown.id }} fixed={['project']} />
        <CommandAction path="repo check" title="Check the repository for AI tooling files" preset={{ project: shown.id }} fixed={['project']} />
        <CommandAction path="task move" title="Move a task to another project, or the workspace-wide list" preset={{ '--project': shown.id }} fixed={['--project', '--global']} label="Move it" note="Its plan and record go with it." />
        <CommandAction path="task remove" title="Forget a task entirely" preset={{ '--project': shown.id }} fixed={['--project', '--global']} label="Forget it" danger note="Moving a task to done or dropped is on the board." />
      </div>
    </section>

    <section class="pblock" aria-labelledby="df">
      <h2 class="ls-zone" id="df">Forget project</h2>
      <p class="lead">Loadout can forget {shown.name} on this machine only, or from the workspace for every machine. Its repository and code are never touched.</p>
      <HaloButton label="Forget project…" kind="danger" size="small" cmd={`opens what it would run · ${lineFor('project remove', [shown.id])}`} onpress={() => (forgetting = true)} />
    </section>
  </section>
{:else if slug}
  <p class="calm" role="status">Reading…</p>
{:else if place}
  <section class="dossier" aria-label="Project place">
    {#if place === 'add'}
      <div class="dhead"><h2>Add a repository you have</h2></div>
      <div class="ls-drawers">
        <CommandAction path="project add" title="Register a Git repository on this machine" open />
        <CommandAction path="project clone" title="Clone a project the workspace knows but this machine does not have" />
      </div>
    {:else if place === 'new'}
      <div class="dhead"><h2>Make a new project</h2></div>
      <div class="ls-drawers">
        <CommandAction path="project new" title="A new project, empty or from one you have" open />
        <CommandAction path="start" title="Your first project, step by step with an agent" {onlaunch} />
      </div>
    {:else if place === 'find'}
      <div class="dhead"><h2>Find repositories and stray memory</h2></div>
      <div class="ls-drawers">
        <CommandAction path="project discover" title="Repositories under the folders Loadout looks in" open />
        <CommandAction path="project survey" title="Agent memory no project accounts for" />
        <CommandAction path="drift" title="Where every project has drifted from what was recorded" />
        <CommandAction path="repo check" title="AI tooling files tracked in a repository" />
      </div>
    {:else if place === 'tasks'}
      <div class="dhead"><h2>Tasks for no project yet</h2></div>
      <p class="lead">The workspace-wide list, for work that does not belong to a project yet.</p>
      <ListToolbar noun="tasks" query={globalList.text} shape={globalList.shape} shown={globalList.tasks?.length ?? null} recorded={globalList.recorded} problem={globalList.problem} onchange={(query) => void globalList.ask(query)} />
      {#if globalList.tasks}
        <TaskBoard tasks={globalList.tasks} away={globalList.away} where={['--global']} from={globalList.from} disabledReason={refused} ondeclare={(task, state) => void act('task declare', declareWords(task.id, state, ['--global']))} />
      {:else}
        <p class="calm" role="status">Reading…</p>
      {/if}
      <div class="ls-drawers">
        <CommandAction path="task declare" title="Record one" preset={{ '--global': true }} fixed={['--project', '--global']} label="Record it" />
        <CommandAction path="task move" title="Move one onto the project it belongs to" preset={{ '--global': true }} fixed={['--project', '--global', '--to-global']} label="Move it" />
      </div>
    {:else if place === 'workspace'}
      <div class="dhead"><h2>The workspace</h2></div>
      <p class="lead">Where projects, memory, specialists and tasks are kept, and synced between your machines.</p>
      {#if workspace}
        <dl class="facts">
          <div><dt>Remote</dt><dd>{workspace.remote || 'none: this machine only'}</dd></div>
          <div><dt>Here</dt><dd><code>{workspace.localPath}</code>{workspace.cloned ? '' : ' · not cloned yet'}</dd></div>
          <div><dt>Projects</dt><dd>{workspace.projects}</dd></div>
        </dl>
        <p class="ls-from">From <code>$ loadout workspace status</code>.</p>
      {/if}
      <div class="ls-drawers">
        <CommandAction path="workspace sync" title="Bring in what other machines saved" />
        <CommandAction path="workspace save" title="Save and push what changed here" />
        <CommandAction path="workspace open" title="Open its folder" label="Open" />
      </div>
    {/if}
  </section>
{/if}

{#if forgetting && shown}
  <ForgetDialog project={shown} onclose={() => (forgetting = false)} />
{/if}
