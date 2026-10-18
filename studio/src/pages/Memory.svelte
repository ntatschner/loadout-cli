<script lang="ts">
  // Memory: a project's stores on the left, the topic being edited in the
  // middle, and its health and tools on the right. A save carries the
  // version it was read at, so a topic somebody else changed meanwhile is
  // shown beside the edit rather than overwritten.
  import { untrack } from 'svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import HealthList from '../lib/components/HealthList.svelte';
  import StoreList from '../lib/components/StoreList.svelte';
  import TopicEditor from '../lib/components/TopicEditor.svelte';
  import { memory, readJson, SignedOut, type Topic } from '../lib/api';
  import { lineFor } from '../lib/commandLine';
  import type { Finding } from '../lib/health';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';
  import { toasts } from '../lib/toasts.svelte';

  /** focused: just the topic, or just the health and tools, for one opened on its own in Minimal. */
  let { slug, scope, name, focused = null }: { slug: string | null; scope: string | null; name: string | null; focused?: 'topic' | 'tools' | null } = $props();

  const STORES = [
    { key: 'project', label: 'This project', hint: 'Travels with the workspace.' },
    { key: 'user', label: 'Your work, any project', hint: 'Travels with the workspace.' },
    { key: 'machine', label: 'This machine only', hint: 'Never synced.', readOnly: true },
  ];

  type Listing = Awaited<ReturnType<typeof memory.topics>>;

  let projects: { slug: string; name: string }[] = $state([]);
  let listing = $state<Listing | null>(null);
  let topic = $state<Topic | null>(null);
  let problem = $state<string | null>(null);
  let query = $state('');
  let found = $state<{ name: string; description: string; path: string; matched: string[] }[] | null>(null);
  let audit = $state<{ verdict: string; topics: number; findings: { severity: string; kind: string; detail: string; topic?: string }[] } | null>(null);

  const editable = $derived(topic !== null && topic.scope !== 'machine' && !studio.watchOnly);
  const readOnlyReason = $derived(studio.watchOnly ? 'Studio is only watching, so this topic is read-only here.' : "This machine's memory is never synced, and is read-only here.");

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

    memory.projects().then((all) => {
      projects = all;

      if (!gone && !untrack(() => slug) && !focused && all.length && router.route.page === 'memory') {
        router.go('memory', all[0].slug);
      }
    }, fail);

    return () => (gone = true);
  });

  $effect(() => {
    listing = null;

    if (slug) {
      memory.topics(slug).then((read) => (listing = read), fail);
    }
  });

  // What the project's memory needs, read once the project is chosen.
  $effect(() => {
    audit = null;
    found = null;

    if (slug) {
      readJson<NonNullable<typeof audit>>('memory audit', [slug]).then((read) => (audit = read), () => (audit = null));
    }
  });

  $effect(() => {
    topic = null;
    problem = null;

    if (slug && scope && name) {
      memory.read(slug, scope, name).then((read) => (topic = read), fail);
    }
  });

  async function find() {
    if (!slug || !query.trim()) {
      return;
    }

    try {
      found = await readJson('memory find', [query.trim(), '--project', slug]);
    } catch (error) {
      fail(error);
    }
  }

  /** Saves through the daemon's route, as memory replace --expect would from a terminal. */
  async function save(text: string, version: string): Promise<{ saved?: Topic; changed?: Topic }> {
    try {
      return await memory.save(slug!, scope!, name!, text, version);
    } catch (error) {
      fail(error);

      return {};
    }
  }

  async function saved(now: Topic) {
    toasts.add({ level: 'done', text: `Saved ${now.name}`, cmd: '$ loadout workspace save · commits it to the workspace' });
    listing = await memory.topics(slug!);
  }

  /** Which store a found topic is in, from where its file is. */
  const storeOf = (path: string) => (/[\\/]global[\\/]/.test(path) ? 'user' : 'project');
  const stores = $derived(STORES.map((store) => ({ ...store, topics: listing?.topics.filter((one) => one.scope === store.key) ?? [] })));
  const findings = $derived<Finding[]>((audit?.findings ?? []).map((one) => ({ name: one.topic ?? one.kind, severity: one.severity, detail: [one.topic ? one.kind : null, one.detail].filter(Boolean).join(' · ') })));
  const href = (store: string, topicName: string) => `#memory/${encodeURIComponent(slug ?? '')}/${store}/${encodeURIComponent(topicName)}`;
  const showLine = $derived(slug && name ? lineFor('memory show', [name, '--project', slug, ...(scope && scope !== 'project' ? ['--scope', scope] : [])]) : '');
</script>

{#snippet findForm()}
  <form class="mfind" onsubmit={(event) => { event.preventDefault(); void find(); }}>
    <label class="lab" for="memory-find">Find the topics that answer</label>
    <input id="memory-find" class="ls-text" bind:value={query} autocomplete="off" placeholder="a question, in your words" />
    <HaloButton label="Find" size="small" cmd={`${lineFor('memory find', [query.trim() || '<question>', '--project', slug ?? ''])} · reads only`} disabledReason={query.trim() ? undefined : 'say what you are looking for'} onpress={() => void find()} />
  </form>
  {#if found}
    <div class="found">
      {#if found.length}
        <ul class="rowsx">
          {#each found as one (one.path)}
            <li><a href={href(storeOf(one.path), one.name)}><b>{one.name}</b></a><span>{one.description}</span></li>
          {/each}
        </ul>
      {:else}
        <p class="lead">Nothing matches. It matches words rather than meanings, so a topic that says the same thing differently will not come back.</p>
      {/if}
    </div>
  {/if}
{/snippet}

{#if !focused}
<div class="head">
  <div><h1>Memory</h1><span class="fact">{listing ? `${listing.topics.length} topics in ${slug}` : 'what each project remembers, by topic'}</span></div>
  <span class="grow"></span>
  {#if projects.length}
    <div class="psel">
      <label for="memory-project">Project</label>
      <select id="memory-project" class="ls-text" value={slug ?? ''} onchange={(event) => router.go('memory', (event.currentTarget as HTMLSelectElement).value)}>
        {#each projects as one (one.slug)}<option value={one.slug}>{one.name} · {one.slug}</option>{/each}
      </select>
    </div>
  {/if}
</div>
{/if}

{#if problem}<p class="offline" role="alert">{problem}</p>{/if}

<div class={['memgrid', name ? 'open' : '', focused ? 'solo' : ''].filter(Boolean).join(' ')}>
  {#if focused}
    <!-- Only the part an item opened in Minimal shows. -->
  {:else if listing}
    <StoreList {stores} picked={scope && name ? { store: scope, name } : null} {href} agent={listing.agent} />
  {:else}
    <p class="calm" role="status">{slug ? 'Reading…' : 'Choose a project.'}</p>
  {/if}

  {#if focused !== 'tools'}
  <div class="memmid">
    {#if topic && slug}
      {#key `${slug}/${topic.scope}/${topic.name}`}
        <TopicEditor project={slug} {topic} storeLabel={STORES.find((one) => one.key === topic!.scope)?.label} {editable} {readOnlyReason} onsave={save} onsaved={(now) => void saved(now)} />
      {/key}
      <p class="ls-from">From <code>{showLine}</code>: as stored.</p>
      {#if topic.scope === 'project' && !studio.watchOnly}
        <div class="ls-drawers">
          <CommandAction path="memory delete" title="Delete this topic" preset={{ topic: topic.name, project: slug, '--yes': true }} fixed={['topic', 'project']} label="Delete it" danger note="A backup is taken first. Topics that link to it are named and left as they are." />
        </div>
      {/if}
    {:else if name && slug && !problem}
      <p class="calm" role="status">Reading…</p>
    {:else if slug}
      <div class="ihead"><h2>What {slug} remembers</h2><p class="slug">the topics on the left are loadout memory list {slug}; choose one to read or edit it</p></div>
      {@render findForm()}
    {/if}
  </div>

  {/if}

  {#if slug && focused !== 'topic'}
    <aside class="memtools" aria-label="Health and tools">
      <section class="pblock" aria-labelledby="mh">
        <h2 class="ls-zone" id="mh">Health</h2>
        {#if audit}
          <HealthList {findings} verdict={audit.verdict} from={`$ loadout memory audit ${slug}`} />
        {:else}
          <p class="calm" role="status">Checking…</p>
        {/if}
      </section>
      {#if topic}
        <section class="pblock" aria-labelledby="mf">
          <h2 class="ls-zone" id="mf">Find</h2>
          {@render findForm()}
        </section>
      {/if}
      <section class="pblock" aria-labelledby="md">
        <h2 class="ls-zone" id="md">Do</h2>
        <div class="ls-drawers">
          <CommandAction path="memory write" title="Record a fact" preset={{ '--project': slug }} fixed={['--project']} label="Record it" />
          <CommandAction path="memory reindex" title="Rebuild the index from the topics that exist" preset={{ project: slug }} fixed={['project']} label="Rebuild it" />
          <CommandAction path="memory audit" title="Clean up what the audit found" preset={{ project: slug, '--clean': true }} fixed={['project']} label="Clean it" />
          <CommandAction path="memory import" title="Bring in what the agent recorded outside the workspace" preset={{ project: slug }} fixed={['project']} label="Import" />
          <CommandAction path="memory compress" title="Move durable facts out of always-loaded instructions" preset={{ PROJECT: slug }} fixed={['PROJECT']} label="Compress" />
          <CommandAction path="memory review" title="Walk the topics nobody has revisited, keeping or expiring each" preset={{ project: slug }} fixed={['project']} />
        </div>
        <p class="ls-from">Clean, import and compress only report what they would do until you tick <code>--apply</code>.</p>
      </section>
    </aside>
  {/if}
</div>
