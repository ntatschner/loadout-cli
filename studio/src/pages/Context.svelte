<script lang="ts">
  // Context: what a session on a project would start with, as one bar of
  // specialists against the token budget, for a task and a mode you choose;
  // then the specialists, the rules and styles a project carries, whether
  // its documents still describe it, your writing voice and handing over.
  // Read with the commands a terminal uses, so this page and 'loadout
  // instructions explain' cannot disagree.
  import { untrack } from 'svelte';
  import Command from '../lib/components/Command.svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import HealthList from '../lib/components/HealthList.svelte';
  import LoadStack from '../lib/components/LoadStack.svelte';
  import Signal from '../lib/components/Signal.svelte';
  import { readJson, SignedOut } from '../lib/api';
  import { lineFor } from '../lib/commandLine';
  import type { Finding } from '../lib/health';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';

  /** focused: just the section, for one opened on its own in Minimal. */
  let { rest, focused = false }: { rest: string[]; focused?: boolean } = $props();

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

  interface Style {
    name: string;
    /** Set only for a style in force: personal, named or codebase. */
    layer?: string | null;
    coreTokens?: number;
    languages?: string[];
  }

  const SECTIONS = [
    { key: 'load', label: 'What a session loads' },
    { key: 'specialists', label: 'Specialists' },
    { key: 'rules', label: 'Rules and styles' },
    { key: 'docs', label: 'Documents' },
    { key: 'health', label: 'Health' },
    { key: 'voice', label: 'Your writing voice' },
    { key: 'handoff', label: 'Handing over' },
  ];
  const MODES = ['implement', 'advise', 'investigate', 'review'];

  let projects = $state<{ id: string; name: string; available?: boolean }[] | null>(null);
  let explained = $state<Explained | null>(null);
  let task = $state('');
  let mode = $state('implement');
  let specialists = $state<Specialist[] | null>(null);
  let kind = $state('');
  let shown = $state<Shown | null>(null);
  let rules = $state<{ list: { path?: string; name?: string; bytes?: number; globs?: string[] }[]; budget: { alwaysLoadedBytes: number; scopedBytes: number; overBudget: boolean }; audit: { verdict: string; findings: { rule?: string; severity?: string; kind?: string; detail?: string }[] } } | null>(null);
  let styles = $state<Style[] | null>(null);
  let profiles = $state<{ name: string; description: string }[] | null>(null);
  let candidates = $state<{ path?: string; file?: string; reason?: string }[] | null>(null);
  let docs = $state<{ documents: number; findings: { path?: string; line?: number; kind?: string; detail?: string }[] } | null>(null);
  let health = $state<{ audit: { checks: number; applicable: number; findings: { specialist?: string; rule?: string; occurrences?: number; caveat?: string }[] }; validate: { specialists: number; errors: number; warnings: number; findings: { specialist?: string; severity?: string; kind?: string; detail?: string }[] } } | null>(null);
  let voice = $state<{ guide: { id: string; version: number; summary: string; reference: string }; layers: { layer: string; name: string; exists: boolean; rules: number }[]; rules: Record<string, unknown>[] } | null>(null);
  let runs = $state<Record<string, unknown>[] | null>(null);
  let guides = $state<{ reference: string; id: string; version: number; summary: string; ships: boolean }[] | null>(null);
  // Which specialists instructions list says are always loaded, for the bar's hatching.
  let always = $state<Set<string> | null>(null);
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
    // A read that ends after the page has gone must not send the browser back here.
    let gone = false;

    readJson<{ projects: { id: string; name: string; available?: boolean }[] }>('project list').then((read) => {
      projects = read.projects;

      if (!gone && !untrack(() => slug) && !focused && read.projects.length && router.route.page === 'context') {
        router.go('context', read.projects[0].id);
      }
    }, fail);

    return () => (gone = true);
  });

  const explainWords = $derived([...(task.trim() ? [task.trim()] : []), '--project', slug ?? '', '--mode', mode]);

  async function explain() {
    if (!slug) {
      return;
    }

    try {
      explained = await readJson<Explained>('instructions explain', explainWords);
    } catch (error) {
      fail(error);
    }
  }

  async function load(project: string, where: string, id: string | null) {
    problem = null;

    try {
      if (where === 'load') {
        readJson<{ specialists: Specialist[] }>('instructions list', [project]).then(
          (read) => (always = new Set(read.specialists.filter((one) => one.always).map((one) => one.id))),
          () => (always = null),
        );
        await explain();
      } else if (where === 'specialists') {
        specialists = (await readJson<{ specialists: Specialist[] }>('instructions list', [project, ...(kind ? ['--kind', kind] : [])])).specialists;
      } else if (where === 'specialist' && id) {
        shown = await readJson<Shown>('instructions show', [id, '--project', project]);
      } else if (where === 'rules') {
        const [list, budget, audit, style, profile] = await Promise.all([
          readJson<{ rules: { path?: string; name?: string; bytes?: number; globs?: string[] }[] }>('rules list', [project]),
          readJson<{ alwaysLoadedBytes: number; scopedBytes: number; overBudget: boolean }>('rules budget', [project]),
          readJson<NonNullable<typeof rules>['audit']>('rules audit', [project]),
          readJson<{ styles: Style[] }>('style list', [project]),
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
      } else if (where === 'voice') {
        const [read, ran, listed] = await Promise.all([
          readJson<NonNullable<typeof voice>>('voice show', ['--project', project]),
          readJson<{ runs: Record<string, unknown>[] }>('voice runs', ['--project', project]),
          readJson<{ guides: NonNullable<typeof guides> }>('voice guide list'),
        ]);
        voice = read;
        runs = ran.runs;
        guides = listed.guides;
      } else if (where === 'health') {
        const [audit, validate] = await Promise.all([
          readJson<NonNullable<typeof health>['audit']>('instructions audit', ['--project', project]),
          readJson<NonNullable<typeof health>['validate']>('instructions validate', [project]),
        ]);
        health = { audit, validate };
      }
    } catch (error) {
      fail(error);
    }
  }

  $effect(() => {
    if (slug) {
      const [project, where, id] = [slug, section, specialist];

      untrack(() => void load(project, where, id));
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
  const loaded = $derived(explained ? explained.selected.map((one) => ({ ...one, always: always?.has(one.id) ?? false })) : []);
  const href = (id: string) => `#context/${encodeURIComponent(slug ?? '')}/specialist/${encodeURIComponent(id)}`;

  // Findings from each audit in HealthList's one shape. The documents and
  // convention audits give no severity: a reference adrift and a departure
  // from what a specialist asks are each something to look at, a warning.
  const ruleFindings = $derived<Finding[]>((rules?.audit.findings ?? []).map((one) => ({ name: one.rule ?? one.kind, severity: one.severity, detail: one.detail })));
  const docFindings = $derived<Finding[]>((docs?.findings ?? []).map((one) => ({ name: one.path ? `${one.path}${one.line ? `:${one.line}` : ''}` : one.kind, severity: 'warning', detail: [one.kind, one.detail].filter(Boolean).join(' · ') })));
  const auditFindings = $derived<Finding[]>((health?.audit.findings ?? []).map((one) => ({ name: one.specialist, severity: 'warning', detail: [one.rule, one.occurrences ? `${one.occurrences} place${one.occurrences === 1 ? '' : 's'}` : null, one.caveat].filter(Boolean).join(' · ') })));
  const validateFindings = $derived<Finding[]>((health?.validate.findings ?? []).map((one) => ({ name: one.specialist ?? one.kind, severity: one.severity, detail: one.detail })));

  function activation(one: Shown['activation']): string {
    if (one.always) {
      return 'Always: every session on every project starts with it.';
    }

    const parts = [
      one.globs.length ? `for files like ${one.globs.join(', ')}` : '',
      one.dependencies.length ? `when the project depends on ${one.dependencies.join(', ')}` : '',
      one.taskPhrases.length ? `for tasks that say ${one.taskPhrases.join(', ')}` : '',
      one.modes.length ? `in ${one.modes.join(', ')} mode` : '',
    ].filter(Boolean);

    return parts.length ? `It loads ${parts.join('; ')}.` : 'It loads only when asked for by name.';
  }

  const bytes = (n: number) => n.toLocaleString('en-GB');
</script>

{#if !focused}
<div class="head">
  <div><h1>Context</h1><span class="fact">what a session reads before it starts, and what it costs</span></div>
  <span class="grow"></span>
  {#if projects}
    <div class="psel">
      <label for="context-project">Project</label>
      <select id="context-project" class="ls-text" value={slug ?? ''} onchange={(event) => router.go('context', (event.currentTarget as HTMLSelectElement).value, section === 'specialist' ? 'specialists' : section)}>
        {#each projects as one (one.id)}<option value={one.id}>{one.name} · {one.id}</option>{/each}
      </select>
    </div>
  {/if}
</div>

<nav class="subnav" aria-label="Context">
  {#each SECTIONS as one (one.key)}
    <a href={`#context/${encodeURIComponent(slug ?? '')}/${one.key}`} aria-current={section === one.key || (one.key === 'specialists' && section === 'specialist') ? 'page' : undefined}>{one.label}</a>
  {/each}
</nav>
{/if}

<div class="ctx">
  {#if problem}<p class="offline" role="alert">{problem}</p>{/if}

  {#if !slug}
    <p class="calm" role="status">Reading…</p>
  {:else if section === 'load'}
    <form class="explain" onsubmit={(event) => { event.preventDefault(); void explain(); }}>
      <div class="grow">
        <label class="lab" for="context-task">The task, in a sentence (optional)</label>
        <input id="context-task" class="ls-text" bind:value={task} autocomplete="off" placeholder="What the session is for" />
      </div>
      <fieldset class="modes">
        <legend class="lab">Mode</legend>
        {#each MODES as one (one)}<label><input type="radio" name="context-mode" value={one} bind:group={mode} />{one}</label>{/each}
      </fieldset>
      <HaloButton label="Explain it" size="small" cmd={`${lineFor('instructions explain', explainWords)} · reads only`} onpress={() => void explain()} />
    </form>
    {#if explained}
      <LoadStack selected={loaded} omitted={explained.omitted} conflicts={explained.conflicts.length}
        estimatedTokens={explained.context.estimatedTokens} tokenBudget={explained.context.tokenBudget} nearBudget={explained.context.nearBudget} {href} />
      <p class="ls-from">From <code>{lineFor('instructions explain', explainWords)}</code>.</p>
    {:else}
      <p class="calm" role="status">Reading…</p>
    {/if}
    <div class="ls-drawers pblock">
      <CommandAction path="instructions explain" title="Compare it with another mode, task or profile" preset={{ '--project': slug }} fixed={['--project']} label="Compare" />
    </div>
  {:else if section === 'specialists'}
    {#if specialists}
      <div class="psel inline">
        <label for="specialist-kind">Kind</label>
        <select id="specialist-kind" class="ls-text" bind:value={kind} onchange={() => slug && void load(slug, 'specialists', null)}>
          <option value="">every kind</option>
          {#each kinds as one (one)}<option value={one}>{one}</option>{/each}
        </select>
      </div>
      <div class="specs">
        {#each specialists as one (one.id)}
          <a class="spec" href={href(one.id)}>
            <span class="k">{one.kind}{one.always ? ' · always' : ''}</span>
            <b>{one.title}</b>
            <span class="s">{one.summary}</span>
            <span class="n">{one.estimatedTokens} tokens · {one.origin}</span>
          </a>
        {/each}
      </div>
      <p class="ls-from">From <code>$ loadout instructions list {slug}</code>.</p>
    {:else}
      <p class="calm" role="status">Reading…</p>
    {/if}
    <div class="ls-drawers pblock">
      <CommandAction path="instructions new" title="Draft a new specialist or skill" preset={{ '--project': slug }} />
      <CommandAction path="instructions export" title="Copy a built-in one into the workspace to edit" />
    </div>
  {:else if section === 'specialist' && shown}
    <p class="back"><a href={`#context/${encodeURIComponent(slug)}/specialists`}>← Specialists</a></p>
    <div class="ihead"><h2>{shown.title}</h2><p class="slug">{shown.id} · {shown.origin} · {shown.estimatedTokens} tokens</p></div>
    <p class="lead">{shown.summary}</p>
    <p class="lead">{activation(shown.activation)}</p>
    <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
    <pre class="body" tabindex="0" aria-label="What it says">{shown.body}</pre>
    <Command why="The same, in a terminal:" command={lineFor('instructions show', [shown.id, '--project', slug])} />
    <div class="ls-drawers pblock">
      <CommandAction path="instructions export" title="Copy it into the workspace to edit" preset={{ SPECIALIST: shown.id, '--project': slug }} fixed={['SPECIALIST']} label="Copy it" />
      <CommandAction path="instructions probe" title="How often sessions did what it asks" preset={{ specialist: shown.id }} fixed={['specialist']} label="Measure it" />
    </div>
  {:else if section === 'specialist'}
    <p class="calm" role="status">Reading…</p>
  {:else if section === 'rules'}
    {#if rules}
      <div class="bytes">
        <p><b>{bytes(rules.budget.alwaysLoadedBytes)}</b>bytes always loaded</p>
        <p><b>{bytes(rules.budget.scopedBytes)}</b>bytes scoped to the files they cover{rules.budget.overBudget ? ', over the budget' : ''}</p>
      </div>
      <section class="pblock" aria-labelledby="cr">
        <h2 class="ls-zone" id="cr">Rules</h2>
        {#if rules.list.length}
          <ul class="rowsx">{#each rules.list as one, index (index)}<li><b>{one.name ?? one.path}</b>{#if one.globs?.length}<code>{one.globs.join(', ')}</code>{/if}{#if one.bytes}<span>{bytes(one.bytes)} bytes</span>{/if}</li>{/each}</ul>
        {:else}
          <p class="lead">No path-scoped rules.</p>
        {/if}
        <h3 class="sub">The audit</h3>
        <HealthList findings={ruleFindings} verdict={rules.audit.verdict} from={`$ loadout rules list ${slug}, loadout rules budget ${slug} and loadout rules audit ${slug}`} />
      </section>
    {/if}
    <section class="pblock" aria-labelledby="cs">
      <h2 class="ls-zone" id="cs">Coding styles</h2>
      {#if styles}
        {#if styles.length}
          <ul class="rowsx">
            {#each styles as one (one.name)}
              <li><b>{one.name}</b>{#if one.layer}<Signal word="met" /><span>in force · {one.layer}</span>{:else}<span>in the library, not in force</span>{/if}{#if one.coreTokens}<code>{one.coreTokens} tokens</code>{/if}</li>
            {/each}
          </ul>
        {:else}
          <p class="lead">No coding styles yet.</p>
        {/if}
        <p class="ls-from">From <code>$ loadout style list {slug}</code>.</p>
      {/if}
    </section>
    <section class="pblock" aria-labelledby="cp">
      <h2 class="ls-zone" id="cp">Profiles</h2>
      {#if profiles}
        <ul class="rowsx">{#each profiles as one (one.name)}<li><b>{one.name}</b><span>{one.description}</span></li>{/each}</ul>
        <p class="ls-from">From <code>$ loadout profile list {slug}</code>.</p>
      {/if}
    </section>
    <section class="pblock" aria-labelledby="cg">
      <h2 class="ls-zone" id="cg">Guidance for everybody</h2>
      {#if candidates?.length}
        <ul class="rowsx">{#each candidates as one, index (index)}<li><code>{one.path ?? one.file}</code>{#if one.reason}<span>{one.reason}</span>{/if}</li>{/each}</ul>
      {:else if candidates}
        <p class="lead">Nothing here looks like it belongs to every project.</p>
      {/if}
      <p class="ls-from">Guidance that never mentions this project, from <code>$ loadout share candidates --project {slug}</code>.</p>
      <div class="ls-drawers">
        <CommandAction path="style use" title="Choose the style this project uses" preset={{ '--project': slug }} fixed={['--project']} label="Use it" />
        <CommandAction path="style show" title="Read a coding style" preset={{ '--project': slug }} fixed={['--project']} label="Show" />
        <CommandAction path="rules split" title="Split an oversized instruction file into scoped rules" preset={{ project: slug }} fixed={['project']} />
        <CommandAction path="share promote" title="Move guidance into the workspace's global layer" label="Move it" />
      </div>
    </section>
  {:else if section === 'docs'}
    {#if docs}
      <p class="lead">{docs.documents} document{docs.documents === 1 ? '' : 's'} checked; {docs.findings.length ? `${docs.findings.length} place${docs.findings.length === 1 ? '' : 's'} where they have come adrift` : 'every reference resolves'}.</p>
      <HealthList findings={docFindings} from={`$ loadout docs audit ${slug}`} noneText="Every reference resolves." />
    {:else}
      <p class="calm" role="status">Checking…</p>
    {/if}
    <div class="ls-drawers pblock">
      <CommandAction path="docs find" title="Where a type or member is declared" preset={{ '--project': slug }} fixed={['--project']} label="Find it" />
      <CommandAction path="docs refresh" title="Bring the symbol index up to date" preset={{ '--project': slug }} fixed={['--project']} />
      <CommandAction path="docs export" title="Write a reference or guide" preset={{ '--project': slug }} fixed={['--project']} />
      <CommandAction path="docs ci" title="Write a CI workflow that regenerates them" preset={{ '--project': slug }} fixed={['--project']} />
    </div>
  {:else if section === 'health'}
    {#if health}
      <p class="lead">{health.audit.applicable} specialists apply here, and {health.audit.checks} checks ran: {health.audit.findings.length ? `${health.audit.findings.length} place${health.audit.findings.length === 1 ? '' : 's'} where the project departs from what they ask` : 'nothing departs from what they ask'}.</p>
      <HealthList findings={auditFindings} from={`$ loadout instructions audit --project ${slug}`} noneText="Nothing departs from what the specialists ask." />
      <h3 class="sub">The library</h3>
      <p class="lead">The library has {health.validate.specialists} specialists: {health.validate.errors} error{health.validate.errors === 1 ? '' : 's'}, {health.validate.warnings} warning{health.validate.warnings === 1 ? '' : 's'}.</p>
      <HealthList findings={validateFindings} from={`$ loadout instructions validate ${slug}`} noneText="Every specialist reads as it should." />
    {:else}
      <p class="calm" role="status">Checking…</p>
    {/if}
    <div class="ls-drawers pblock">
      <CommandAction path="instructions stats" title="Which specialists launches actually reached" preset={{ '--project': slug }} fixed={['--project']} label="Show" />
      <CommandAction path="instructions probe" title="How often sessions did what a specialist asks" />
    </div>
  {:else if section === 'voice'}
    <p class="lead">How you write, learned from your own writing and checked against it. The rules in force are rendered into the style specialist your sessions load.</p>
    {#if voice}
      <dl class="facts">
        <dt>Guide</dt><dd>{voice.guide.reference} · {voice.guide.summary}</dd>
        <dt>Layers</dt><dd>{voice.layers.map((one) => `${one.name}: ${one.exists ? `${one.rules} rule${one.rules === 1 ? '' : 's'}` : 'not learned yet'}`).join(' · ')}</dd>
        {#if runs}<dt>Learn runs</dt><dd>{runs.length} so far, as <code>loadout voice runs</code> lists them</dd>{/if}
      </dl>
      {#if voice.rules.length}
        <ul class="rowsx">
          {#each voice.rules as rule, index (index)}
            <li>{Object.values(rule).filter((value) => typeof value === 'string').slice(0, 3).join(' · ')}</li>
          {/each}
        </ul>
      {/if}
      <p class="ls-from">From <code>$ loadout voice show --project {slug}</code>.</p>
      {#if guides}
        <p class="ls-from">Guides here: {guides.map((one) => `${one.reference}${one.ships ? '' : ' (yours)'}`).join(', ')}, from <code>$ loadout voice guide list</code>.</p>
      {/if}
    {:else}
      <p class="calm" role="status">Reading…</p>
    {/if}
    <div class="ls-drawers pblock">
      <h4 class="gh">Check and mark</h4>
      <CommandAction path="voice check" title="Check a message or a file against it" preset={{ '--project': slug }} fixed={['--project']} label="Check it" />
      <CommandAction path="voice mark" title="Mark writing as yours, so learning counts it" preset={{ '--project': slug }} fixed={['--project']} label="Mark it" />
      <CommandAction path="voice render" title="Render it into the style specialist sessions load" preset={{ '--project': slug }} fixed={['--project']} label="Render it" />
      <h4 class="gh">Learn, ask as they go</h4>
      <CommandAction path="voice learn" title="Learn it from your own writing, rule by rule" preset={{ '--project': slug }} fixed={['--project']} />
      <CommandAction path="voice guide draft" title="Draft a new guide with an agent" />
      <h4 class="gh">Read</h4>
      <CommandAction path="voice runs" title="Past learn runs, with what each cost" preset={{ '--project': slug }} fixed={['--project']} label="Show" />
      <CommandAction path="voice guide show" title="A guide's sources, checks and thresholds" label="Show" />
    </div>
  {:else if section === 'handoff'}
    <p class="lead">A handoff is a note the next session reads first: what this one worked out, so it does not have to again.</p>
    <div class="ls-drawers">
      <CommandAction path="handoff" title="Write or read a handoff" preset={{ project: slug }} fixed={['project']} open />
    </div>
  {/if}
</div>
