<script lang="ts">
  // What a session is given before it starts, and what that costs: the
  // specialists a task would load and why, the rules and styles a project
  // carries, its profiles, and whether its documents still describe it.
  // Read with the commands a terminal uses, so this page and 'loadout
  // instructions explain' cannot disagree.
  import { untrack } from 'svelte';
  import Command from '../lib/components/Command.svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import Meter from '../lib/components/Meter.svelte';
  import { readJson, SignedOut } from '../lib/api';
  import { lineFor } from '../lib/commandLine';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';

  let { rest }: { rest: string[] } = $props();

  interface Selected {
    id: string;
    kind: string;
    title: string;
    reason: string;
    confidence: number;
    origin: string;
    estimatedTokens: number;
  }

  interface Explained {
    project: string;
    mode: string;
    selected: Selected[];
    omitted: { id: string; reason: string }[];
    conflicts: { winner?: string; loser?: string; subject?: string }[];
    context: { estimatedTokens: number; tokenBudget: number; overBudget: boolean; nearBudget: boolean };
  }

  interface Specialist {
    id: string;
    kind: string;
    title: string;
    summary: string;
    origin: string;
    estimatedTokens: number;
    always: boolean;
  }

  interface Shown {
    id: string;
    title: string;
    summary: string;
    origin: string;
    estimatedTokens: number;
    activation: { always: boolean; globs: string[]; dependencies: string[]; taskPhrases: string[]; modes: string[] };
    body: string;
  }

  const SECTIONS = [
    { key: 'load', label: 'What a session would load' },
    { key: 'specialists', label: 'Specialists' },
    { key: 'rules', label: 'Rules and coding styles' },
    { key: 'docs', label: 'Documents' },
    { key: 'health', label: 'Health' },
    { key: 'handoff', label: 'Handing over' },
  ];
  const MODES = ['implement', 'advise', 'investigate', 'review'];

  let projects = $state<{ id: string; name: string }[] | null>(null);
  let explained = $state<Explained | null>(null);
  let task = $state('');
  let mode = $state('implement');
  let specialists = $state<Specialist[] | null>(null);
  let kind = $state('');
  let shown = $state<Shown | null>(null);
  let rules = $state<{ list: { path?: string; name?: string; bytes?: number; globs?: string[] }[]; budget: { alwaysLoadedBytes: number; scopedBytes: number; overBudget: boolean }; audit: { verdict: string; findings: { message?: string; detail?: string }[] } } | null>(null);
  let styles = $state<{ name: string; inForce?: boolean; description?: string }[] | null>(null);
  let profiles = $state<{ name: string; description: string }[] | null>(null);
  let candidates = $state<{ path?: string; file?: string; reason?: string }[] | null>(null);
  let docs = $state<{ documents: number; findings: { path?: string; line?: number; message?: string; detail?: string }[] } | null>(null);
  let health = $state<{ audit: { checks: number; applicable: number; findings: { message?: string; detail?: string }[] }; validate: { specialists: number; errors: number; warnings: number; findings: { id?: string; message?: string }[] } } | null>(null);
  let problem = $state<string | null>(null);

  // #context/<project>/<section>, and #context/<project>/specialist/<id>.
  const slug = $derived(rest[0] ?? null);
  const section = $derived(rest[1] ?? 'load');
  const specialist = $derived(section === 'specialist' ? (rest[2] ?? null) : null);

  function fail(error: unknown) {
    if (error instanceof SignedOut) {
      studio.signedOut = true;
    } else {
      problem = (error as Error).message;
    }
  }

  $effect(() => {
    readJson<{ projects: { id: string; name: string }[] }>('project list').then((read) => {
      projects = read.projects;

      if (!slug && read.projects.length) {
        router.go('context', read.projects[0].id);
      }
    }, fail);
  });

  async function explain() {
    if (!slug) {
      return;
    }

    try {
      explained = await readJson<Explained>('instructions explain', [...(task.trim() ? [task.trim()] : []), '--project', slug, '--mode', mode]);
    } catch (error) {
      fail(error);
    }
  }

  async function load(project: string, where: string, id: string | null) {
    problem = null;

    try {
      if (where === 'load') {
        await explain();
      } else if (where === 'specialists') {
        specialists = (await readJson<{ specialists: Specialist[] }>('instructions list', [project, ...(kind ? ['--kind', kind] : [])])).specialists;
      } else if (where === 'specialist' && id) {
        shown = await readJson<Shown>('instructions show', [id, '--project', project]);
      } else if (where === 'rules') {
        const [list, budget, audit, style, profile] = await Promise.all([
          readJson<{ rules: { path?: string; name?: string; bytes?: number; globs?: string[] }[] }>('rules list', [project]),
          readJson<{ alwaysLoadedBytes: number; scopedBytes: number; overBudget: boolean }>('rules budget', [project]),
          readJson<{ verdict: string; findings: { message?: string; detail?: string }[] }>('rules audit', [project]),
          readJson<{ styles: { name: string; inForce?: boolean; description?: string }[] }>('style list', [project]),
          readJson<{ profiles: { name: string; description: string }[] }>('profile list', [project]),
        ]);
        rules = { list: list.rules, budget, audit };
        styles = style.styles;
        profiles = profile.profiles;
        readJson<{ candidates?: { path?: string; file?: string; reason?: string }[] } | { path?: string; file?: string; reason?: string }[]>('share candidates', ['--project', project]).then(
          (read) => (candidates = Array.isArray(read) ? read : (read.candidates ?? [])),
          () => (candidates = null),
        );
      } else if (where === 'docs') {
        docs = await readJson('docs audit', [project]);
      } else if (where === 'health') {
        const [audit, validate] = await Promise.all([
          readJson<{ checks: number; applicable: number; findings: { message?: string; detail?: string }[] }>('instructions audit', ['--project', project]),
          readJson<{ specialists: number; errors: number; warnings: number; findings: { id?: string; message?: string }[] }>('instructions validate', [project]),
        ]);
        health = { audit, validate };
      }
    } catch (error) {
      fail(error);
    }
  }

  $effect(() => {
    if (slug) {
      void load(slug, section, specialist);
    }
  });

  const settled = $derived(studio.jobs.filter((job) => job.state !== 'running').length);
  let seen = untrack(() => settled);

  $effect(() => {
    if (settled === seen) {
      return;
    }

    seen = settled;

    untrack(() => {
      if (slug) {
        void load(slug, section, specialist);
      }
    });
  });

  const kinds = $derived([...new Set((specialists ?? []).map((one) => one.kind))]);
  const explainWords = $derived([...(task.trim() ? [task.trim()] : []), '--project', slug ?? '', '--mode', mode]);
  const words = (finding: { message?: string; detail?: string; path?: string; line?: number; id?: string }) =>
    [finding.id, finding.path ? `${finding.path}${finding.line ? `:${finding.line}` : ''}` : null, finding.message, finding.detail].filter(Boolean).join(' · ');
</script>

<div class="head"><div><h1>Context</h1><span class="fact">what a session reads before it starts, and what it costs</span></div></div>

<div class="memory">
  <aside class="memside" aria-label="Context">
    {#if projects}
      <label class="fld find"><span class="lab">Project</span>
        <select name="context-project" value={slug ?? ''} onchange={(event) => router.go('context', (event.currentTarget as HTMLSelectElement).value, section === 'specialist' ? 'specialists' : section)}>
          {#each projects as one (one.id)}<option value={one.id}>{one.name}</option>{/each}
        </select>
      </label>
    {/if}
    <ul class="slist">
      {#each SECTIONS as one (one.key)}
        <li><a class="srow" href={`#context/${encodeURIComponent(slug ?? '')}/${one.key}`} aria-current={section === one.key || (one.key === 'specialists' && section === 'specialist') ? 'page' : undefined}><span class="t">{one.label}</span></a></li>
      {/each}
    </ul>
  </aside>

  <section class="memmain" aria-label="Context">
    {#if problem}<p class="offline" role="alert">{problem}</p>{/if}

    {#if !slug}
      <p class="calm" role="status">Reading…</p>
    {:else if section === 'load'}
      <h2 class="topicname">What a session on {slug} would load</h2>
      <form class="cmdform" onsubmit={(event) => { event.preventDefault(); void explain(); }}>
        <label class="fld"><span class="lab">The task, in a sentence (optional)</span><input name="context-task" bind:value={task} autocomplete="off" /></label>
        <label class="fld"><span class="lab">Mode</span>
          <select name="context-mode" bind:value={mode}>{#each MODES as one (one)}<option value={one}>{one}</option>{/each}</select>
        </label>
        <div class="ls-answers flush"><HaloButton label="Explain it" solid size="small" cmd={lineFor('instructions explain', explainWords)} onpress={() => void explain()} /></div>
      </form>
      {#if explained}
        <Meter label="Tokens every session starts with" value={explained.context.estimatedTokens} max={Math.max(explained.context.tokenBudget, explained.context.estimatedTokens)} threshold={explained.context.tokenBudget} thresholdLabel="budget" overWords="over the budget" unit="tokens" />
        <h3 class="ls-zone">Loaded</h3>
        <ul class="findings">
          {#each explained.selected as one (one.id)}
            <li class="info"><a href={`#context/${encodeURIComponent(slug)}/specialist/${encodeURIComponent(one.id)}`}><span class="n">{one.title}</span></a> <span class="sev">{one.id}</span> · {one.reason} · {one.estimatedTokens} tokens</li>
          {/each}
        </ul>
        {#if explained.omitted.length}
          <h3 class="ls-zone">Considered and left out</h3>
          <ul class="plain">{#each explained.omitted as one (one.id)}<li><span class="n">{one.id}</span> · {one.reason}</li>{/each}</ul>
        {/if}
        {#if explained.conflicts.length}<p class="calm small">{explained.conflicts.length} overlap{explained.conflicts.length === 1 ? '' : 's'} settled; the narrower one wins.</p>{/if}
      {/if}
      <div class="acts">
        <CommandAction path="instructions explain" title="Compare it with another mode, task or profile" preset={{ '--project': slug }} fixed={['--project']} label="Compare" />
      </div>
    {:else if section === 'specialists'}
      <h2 class="topicname">Specialists</h2>
      {#if specialists}
        <label class="fld"><span class="lab">Kind</span>
          <select name="specialist-kind" bind:value={kind} onchange={() => slug && void load(slug, 'specialists', null)}>
            <option value="">every kind</option>
            {#each kinds as one (one)}<option value={one}>{one}</option>{/each}
          </select>
        </label>
        <ul class="slist">
          {#each specialists as one (one.id)}
            <li><a class="srow" href={`#context/${encodeURIComponent(slug)}/specialist/${encodeURIComponent(one.id)}`}><span class="t">{one.title}{one.always ? ' · always' : ''}</span><span class="x">{one.id} · {one.estimatedTokens} tokens · {one.summary}</span></a></li>
          {/each}
        </ul>
        <p class="calm small">From <code class="ls-cmd">loadout instructions list {slug}</code>.</p>
      {:else}
        <p class="calm" role="status">Reading…</p>
      {/if}
      <div class="acts">
        <CommandAction path="instructions new" title="Draft a new specialist or skill" preset={{ '--project': slug }} />
        <CommandAction path="instructions export" title="Copy a built-in one into the workspace to edit" />
      </div>
    {:else if section === 'specialist' && shown}
      <h2 class="topicname">{shown.title}</h2>
      <p class="calm">{shown.summary} <span class="effect">· {shown.id} · {shown.origin} · {shown.estimatedTokens} tokens</span></p>
      <dl class="facts">
        <dt>Loads</dt><dd>{shown.activation.always ? 'always' : [shown.activation.globs.length ? `for files like ${shown.activation.globs.join(', ')}` : '', shown.activation.dependencies.length ? `with ${shown.activation.dependencies.join(', ')}` : '', shown.activation.taskPhrases.length ? `for tasks that say ${shown.activation.taskPhrases.join(', ')}` : '', shown.activation.modes.length ? `in ${shown.activation.modes.join(', ')} mode` : ''].filter(Boolean).join('; ') || 'when asked for'}</dd>
      </dl>
      <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
      <pre class="output prose" tabindex="0" aria-label="What it says">{shown.body}</pre>
      <Command why="The same, in a terminal:" command={`$ loadout instructions show ${shown.id} --project ${slug}`} />
      <div class="acts">
        <CommandAction path="instructions export" title="Copy it into the workspace to edit" preset={{ SPECIALIST: shown.id, '--project': slug }} fixed={['SPECIALIST']} label="Copy it" />
        <CommandAction path="instructions probe" title="How often sessions did what it asks" preset={{ specialist: shown.id }} fixed={['specialist']} label="Measure it" />
      </div>
    {:else if section === 'specialist'}
      <p class="calm" role="status">Reading…</p>
    {:else if section === 'rules'}
      <h2 class="topicname">Rules and coding styles</h2>
      {#if rules}
        <p class="calm">{rules.budget.alwaysLoadedBytes} bytes always loaded, {rules.budget.scopedBytes} scoped to the files they cover{rules.budget.overBudget ? ', which is over the budget' : ''}. The audit says <strong>{rules.audit.verdict.toLowerCase()}</strong>.</p>
        {#if rules.list.length}
          <ul class="plain">{#each rules.list as one, index (index)}<li><span class="n">{one.name ?? one.path}</span>{one.globs?.length ? ` · ${one.globs.join(', ')}` : ''}{one.bytes ? ` · ${one.bytes} bytes` : ''}</li>{/each}</ul>
        {:else}
          <p class="calm">No path-scoped rules.</p>
        {/if}
        {#each rules.audit.findings as finding, index (index)}<p class="attention">{words(finding)}</p>{/each}
        <p class="calm small">From <code class="ls-cmd">loadout rules list {slug}</code>, <code class="ls-cmd">loadout rules budget {slug}</code> and <code class="ls-cmd">loadout rules audit {slug}</code>.</p>
      {/if}
      <h3 class="ls-zone">Coding styles</h3>
      {#if styles}
        {#if styles.length}
          <ul class="plain">{#each styles as one (one.name)}<li><span class="n">{one.name}</span>{one.inForce ? ' · in force' : ''}{one.description ? ` · ${one.description}` : ''}</li>{/each}</ul>
        {:else}
          <p class="calm">No coding styles yet.</p>
        {/if}
        <p class="calm small">From <code class="ls-cmd">loadout style list {slug}</code>.</p>
      {/if}
      <h3 class="ls-zone">Profiles</h3>
      {#if profiles}
        <ul class="plain">{#each profiles as one (one.name)}<li><span class="n">{one.name}</span> · {one.description}</li>{/each}</ul>
        <p class="calm small">From <code class="ls-cmd">loadout profile list {slug}</code>.</p>
      {/if}
      <h3 class="ls-zone">Guidance for everybody</h3>
      {#if candidates?.length}
        <ul class="plain">{#each candidates as one, index (index)}<li><code class="ls-cmd">{one.path ?? one.file}</code>{one.reason ? ` · ${one.reason}` : ''}</li>{/each}</ul>
      {:else if candidates}
        <p class="calm">Nothing here looks like it belongs to every project.</p>
      {/if}
      <p class="calm small">Guidance that never mentions this project, from <code class="ls-cmd">loadout share candidates --project {slug}</code>.</p>
      <div class="acts">
        <CommandAction path="share promote" title="Move guidance into the workspace's global layer" label="Move it" />
        <CommandAction path="rules split" title="Split an oversized instruction file into scoped rules" preset={{ project: slug }} fixed={['project']} />
        <CommandAction path="style show" title="Read a coding style" preset={{ '--project': slug }} fixed={['--project']} label="Show" />
        <CommandAction path="style use" title="Choose the style this project uses" preset={{ '--project': slug }} fixed={['--project']} label="Use it" />
      </div>
    {:else if section === 'docs'}
      <h2 class="topicname">Documents</h2>
      {#if docs}
        <p class="calm">{docs.documents} document{docs.documents === 1 ? '' : 's'} checked; {docs.findings.length ? `${docs.findings.length} place${docs.findings.length === 1 ? '' : 's'} where they have come adrift` : 'every reference resolves'}.</p>
        {#each docs.findings as finding, index (index)}<p class="attention">{words(finding)}</p>{/each}
        <p class="calm small">From <code class="ls-cmd">loadout docs audit {slug}</code>.</p>
      {:else}
        <p class="calm" role="status">Checking…</p>
      {/if}
      <div class="acts">
        <CommandAction path="docs find" title="Where a type or member is declared" preset={{ '--project': slug }} fixed={['--project']} label="Find it" />
        <CommandAction path="docs refresh" title="Bring the symbol index up to date" preset={{ '--project': slug }} fixed={['--project']} />
        <CommandAction path="docs export" title="Write a reference or guide" preset={{ '--project': slug }} fixed={['--project']} />
        <CommandAction path="docs ci" title="Write a CI workflow that regenerates them" preset={{ '--project': slug }} fixed={['--project']} />
      </div>
    {:else if section === 'health'}
      <h2 class="topicname">Health</h2>
      {#if health}
        <p class="calm">{health.audit.applicable} specialists apply here, and {health.audit.checks} checks ran: {health.audit.findings.length ? `${health.audit.findings.length} place${health.audit.findings.length === 1 ? '' : 's'} where the project departs from what they ask` : 'nothing departs from what they ask'}.</p>
        {#each health.audit.findings as finding, index (index)}<p class="attention">{words(finding)}</p>{/each}
        <p class="calm">The library has {health.validate.specialists} specialists: {health.validate.errors} error{health.validate.errors === 1 ? '' : 's'}, {health.validate.warnings} warning{health.validate.warnings === 1 ? '' : 's'}.</p>
        {#each health.validate.findings as finding, index (index)}<p class="attention">{words(finding)}</p>{/each}
        <p class="calm small">From <code class="ls-cmd">loadout instructions audit --project {slug}</code> and <code class="ls-cmd">loadout instructions validate {slug}</code>.</p>
      {:else}
        <p class="calm" role="status">Checking…</p>
      {/if}
      <div class="acts">
        <CommandAction path="instructions stats" title="Which specialists launches actually reached" preset={{ '--project': slug }} fixed={['--project']} label="Show" />
        <CommandAction path="instructions probe" title="How often sessions did what a specialist asks" />
      </div>
    {:else if section === 'handoff'}
      <h2 class="topicname">Handing over</h2>
      <p class="calm">A handoff is a note the next session reads first: what this one worked out, so it does not have to again.</p>
      <div class="acts">
        <CommandAction path="handoff" title="Write or read a handoff" preset={{ project: slug }} fixed={['project']} open />
      </div>
    {/if}
  </section>
</div>
