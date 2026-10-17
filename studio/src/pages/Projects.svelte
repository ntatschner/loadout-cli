<script lang="ts">
  // The projects Loadout knows on this machine: each one's facts, its
  // working trees and its health, and everything that can be done to it,
  // each through its own command. Read with the same commands a terminal
  // would use, so the page and 'loadout project show' cannot disagree.
  import { untrack } from 'svelte';
  import Command from '../lib/components/Command.svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import { readJson, SignedOut } from '../lib/api';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';

  let { rest, onlaunch }: { rest: string[]; onlaunch: () => void } = $props();

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

  interface Finding {
    name: string;
    severity: string;
    detail: string;
    fixable: boolean;
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

  let projects = $state<Listed[] | null>(null);
  let shown = $state<Shown | null>(null);
  let worktrees = $state<{ path: string; branch: string; primary: boolean }[]>([]);
  let health = $state<Finding[] | null>(null);
  let repository = $state<Repository | null>(null);
  let workspace = $state<Workspace | null>(null);
  let problem = $state<string | null>(null);

  const slug = $derived(rest[0] && !rest[0].startsWith('+') ? rest[0] : null);
  const place = $derived(rest[0]?.startsWith('+') ? rest[0].slice(1) : null);

  function fail(error: unknown) {
    if (error instanceof SignedOut) {
      studio.signedOut = true;
    } else {
      problem = (error as Error).message;
    }
  }

  async function loadList() {
    try {
      projects = (await readJson<{ projects: Listed[] }>('project list')).projects;
    } catch (error) {
      fail(error);
    }
  }

  $effect(() => {
    void loadList();
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
    });
  });

  $effect(() => {
    shown = null;
    health = null;
    repository = null;
    worktrees = [];
    problem = null;

    if (slug) {
      void loadProject(slug);
    }
  });

  $effect(() => {
    if (place === 'workspace') {
      readJson<Workspace>('workspace status').then((read) => (workspace = read), fail);
    }
  });

  async function loadProject(name: string) {
    try {
      shown = await readJson<Shown>('project show', [name]);
    } catch (error) {
      fail(error);

      return;
    }

    // The rest is worth having but not worth holding the facts up for.
    readJson<{ worktrees: typeof worktrees }>('project worktrees', [name]).then((read) => (worktrees = read.worktrees), () => (worktrees = []));
    readJson<{ project: string; findings: Finding[] }[]>('drift', [name]).then((read) => (health = read[0]?.findings ?? []), () => (health = null));
    readJson<{ repositories: Repository[] }>('repo check', [name]).then((read) => (repository = read.repositories[0] ?? null), () => (repository = null));
  }

  const PLACES = [
    { key: 'add', label: 'Add a repository you have' },
    { key: 'new', label: 'Make a new project' },
    { key: 'find', label: 'Find repositories and stray memory' },
    { key: 'workspace', label: 'The workspace' },
  ];
</script>

<div class="head"><div><h1>Projects</h1><span class="fact">{projects ? `${projects.length} on this machine` : 'the projects Loadout knows'}</span></div></div>

<div class="memory">
  <aside class="memside" aria-label="Projects">
    {#if projects === null}
      <p class="calm" role="status">Reading…</p>
    {:else if projects.length === 0}
      <p class="calm">No projects yet. Add a repository you have, or make a new one.</p>
    {:else}
      <ul class="slist">
        {#each projects as one (one.id)}
          <li>
            <a class="srow" href={`#projects/${encodeURIComponent(one.id)}`} aria-current={slug === one.id ? 'page' : undefined}>
              <span class="t">{one.name}</span>
              <span class="x">{one.id} · {one.defaultAgent}{one.available ? '' : ' · not on this machine'}</span>
            </a>
          </li>
        {/each}
      </ul>
      <p class="calm small">The same list as <code class="ls-cmd">loadout project list</code> or <code class="ls-cmd">loadout list</code>.</p>
    {/if}

    <section class="store" aria-labelledby="pj-more">
      <h2 class="ls-zone" id="pj-more">More</h2>
      <ul class="slist">
        {#each PLACES as one (one.key)}
          <li><a class="srow" href={`#projects/+${one.key}`} aria-current={place === one.key ? 'page' : undefined}><span class="t">{one.label}</span></a></li>
        {/each}
      </ul>
    </section>
  </aside>

  <section class="memmain" aria-label="Project">
    {#if problem}<p class="offline" role="alert">{problem}</p>{/if}

    {#if slug && shown}
      <h2 class="topicname">{shown.name}</h2>
      <dl class="facts">
        <dt>Slug</dt><dd><code class="ls-cmd">{shown.id}</code>{#if shown.aliases.length} · also {shown.aliases.join(', ')}{/if}</dd>
        <dt>Where</dt><dd>{#if shown.available}<code class="ls-cmd">{shown.path}</code>{:else}not on this machine{/if}</dd>
        {#if shown.branch}<dt>Branch</dt><dd>{shown.branch}{shown.clean === false ? ' · uncommitted changes' : shown.clean ? ' · clean' : ''}</dd>{/if}
        <dt>Remote</dt><dd>{shown.remote || 'none recorded'}</dd>
        <dt>Agent</dt><dd>{shown.defaultAgent}</dd>
        <dt>Launched</dt><dd>{shown.launchCount} time{shown.launchCount === 1 ? '' : 's'}</dd>
        <dt>Profiles</dt><dd>{shown.profiles.length ? shown.profiles.join(', ') : 'none'}</dd>
        <dt>Every session also gets</dt><dd>{Object.entries(shown.context).filter(([, on]) => on).map(([key]) => key).join(', ') || 'nothing extra'}</dd>
      </dl>
      <Command why="The same, in a terminal:" command={`$ loadout project show ${shown.id}`} />

      {#if worktrees.length > 1}
        <h3 class="ls-zone">Working trees</h3>
        <ul class="plain">
          {#each worktrees as tree (tree.path)}<li><code class="ls-cmd">{tree.path}</code> · {tree.branch}{tree.primary ? ' · the main one' : ''}</li>{/each}
        </ul>
      {/if}

      <h3 class="ls-zone">Health</h3>
      {#if health === null}
        <p class="calm" role="status">Checking…</p>
      {:else}
        <ul class="findings">
          {#each health as finding (finding.name)}
            <li class={finding.severity.toLowerCase()}><span class="sev">{finding.severity}</span> <span class="n">{finding.name}</span> · {finding.detail}{finding.fixable ? ' · can be put right' : ''}</li>
          {/each}
          {#if repository}
            <li class={repository.compliant ? 'info' : 'warning'}><span class="sev">{repository.compliant ? 'Info' : 'Warning'}</span> <span class="n">AI tooling files</span> · {repository.compliant ? 'none tracked in the repository' : `${repository.findings.length} tracked in the repository`}</li>
          {/if}
        </ul>
        <p class="calm small">From <code class="ls-cmd">loadout drift {shown.id}</code> and <code class="ls-cmd">loadout repo check {shown.id}</code>.</p>
      {/if}

      <h3 class="ls-zone">Do</h3>
      <div class="acts">
        {#if health?.some((finding) => finding.fixable)}
          <CommandAction path="drift" title="Put right what can be put right" preset={{ PROJECT: shown.id, '--fix': true, '--yes': true }} fixed={['PROJECT']} label="Put it right" />
        {/if}
        {#if !shown.available}
          <CommandAction path="project clone" title="Clone it onto this machine" preset={{ project: shown.id }} fixed={['project']} />
        {/if}
        <CommandAction path="project onboard" title="Onboard it with an agent" {onlaunch} />
        <CommandAction path="code" title="Open it in the editor" preset={{ project: shown.id }} fixed={['project']} label="Open" />
        <CommandAction path="project open" title="Open its folder" preset={{ project: shown.id }} fixed={['project']} label="Open" />
        <CommandAction path="project context" title="Change what every session also gets" preset={{ '--project': shown.id }} fixed={['--project']} />
        <CommandAction path="project proposal" title="A proposed change to its settings" preset={{ project: shown.id }} fixed={['project']} />
        <CommandAction path="project relocate" title="It has moved on this machine" preset={{ project: shown.id }} fixed={['project']} />
        <CommandAction path="project link" title="Record in the repository which project it is" preset={{ project: shown.id }} fixed={['project']} />
        <CommandAction path="repo check" title="Check the repository for AI tooling files" preset={{ project: shown.id }} fixed={['project']} />
        <CommandAction path="project remove" title="Stop knowing this project" preset={{ project: shown.id }} fixed={['project']} label="Remove" />
      </div>
      <p class="calm small">Removing a project forgets its registration. Its code is never deleted.</p>
    {:else if slug}
      <p class="calm" role="status">Reading…</p>
    {:else if place === 'add'}
      <h2 class="topicname">Add a repository you have</h2>
      <CommandAction path="project add" title="Register a Git repository on this machine" open />
      <CommandAction path="project clone" title="Clone a project the workspace knows but this machine does not have" />
    {:else if place === 'new'}
      <h2 class="topicname">Make a new project</h2>
      <CommandAction path="project new" title="A new project, empty or from one you have" open />
      <CommandAction path="start" title="Your first project, step by step with an agent" {onlaunch} />
    {:else if place === 'find'}
      <h2 class="topicname">Find repositories and stray memory</h2>
      <CommandAction path="project discover" title="Repositories under the folders Loadout looks in" open />
      <CommandAction path="project survey" title="Agent memory no project accounts for" />
      <CommandAction path="drift" title="Where every project has drifted from what was recorded" />
      <CommandAction path="repo check" title="AI tooling files tracked in a repository" />
    {:else if place === 'workspace'}
      <h2 class="topicname">The workspace</h2>
      <p class="calm">Where projects, memory, specialists and tasks are kept, and synced between your machines.</p>
      {#if workspace}
        <dl class="facts">
          <dt>Remote</dt><dd>{workspace.remote || 'none: this machine only'}</dd>
          <dt>Here</dt><dd><code class="ls-cmd">{workspace.localPath}</code>{workspace.cloned ? '' : ' · not cloned yet'}</dd>
          <dt>Projects</dt><dd>{workspace.projects}</dd>
        </dl>
        <Command why="From:" command="$ loadout workspace status" />
      {/if}
      <div class="acts">
        <CommandAction path="workspace sync" title="Bring in what other machines saved" />
        <CommandAction path="workspace save" title="Save and push what changed here" />
        <CommandAction path="workspace open" title="Open its folder" label="Open" />
      </div>
    {:else if projects?.length}
      <p class="calm">Choose a project to see it and what can be done to it.</p>
    {/if}
  </section>
</div>
