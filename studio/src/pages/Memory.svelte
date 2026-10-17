<script lang="ts">
  // What each project remembers, by store and topic, read and edited in
  // place. A save carries the version it was read at, so a topic somebody
  // else changed meanwhile is shown beside the edit rather than overwritten.
  import CommandAction from '../lib/components/CommandAction.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import Picker from '../lib/components/Picker.svelte';
  import { memory, readJson, SignedOut, type Topic } from '../lib/api';
  import { lineFor } from '../lib/commandLine';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';
  import { toasts } from '../lib/toasts.svelte';

  let { slug, scope, name }: { slug: string | null; scope: string | null; name: string | null } = $props();

  const STORES = [
    { key: 'project', label: 'This project', hint: 'Travels with the workspace.' },
    { key: 'user', label: 'Your work, any project', hint: 'Travels with the workspace.' },
    { key: 'machine', label: 'This machine only', hint: 'Never synced. Read-only here.' },
  ];

  type Listing = Awaited<ReturnType<typeof memory.topics>>;

  let projects: { slug: string; name: string }[] = $state([]);
  let listing = $state<Listing | null>(null);
  let topic = $state<Topic | null>(null);
  let draft = $state('');
  let changed = $state<Topic | null>(null);
  let problem = $state<string | null>(null);
  let saving = $state(false);
  let query = $state('');
  let found = $state<{ name: string; description: string; path: string; matched: string[] }[] | null>(null);
  let audit = $state<{ verdict: string; topics: number; findings: { severity: string; kind: string; detail: string; topic?: string }[] } | null>(null);

  const dirty = $derived(topic !== null && draft !== topic.text);
  const editable = $derived(topic !== null && topic.scope !== 'machine' && !studio.watchOnly);
  const replace = $derived(slug && name ? `$ loadout memory replace ${name} --project ${slug}${scope && scope !== 'project' ? ` --scope ${scope}` : ''} --file <path>` : '');

  function fail(error: unknown) {
    if (error instanceof SignedOut) {
      studio.signedOut = true;
    } else {
      problem = (error as Error).message;
    }
  }

  $effect(() => {
    memory.projects().then((all) => {
      projects = all;

      if (!slug && all.length) {
        router.go('memory', all[0].slug);
      }
    }, fail);
  });

  $effect(() => {
    listing = null;

    if (slug) {
      memory.topics(slug).then((found) => (listing = found), fail);
    }
  });

  // What the project's memory needs, read once the project is chosen.
  $effect(() => {
    audit = null;
    found = null;

    if (slug) {
      readJson<typeof audit & object>('memory audit', [slug]).then((read) => (audit = read), () => (audit = null));
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

  /** Which store a found topic is in, from where its file is. */
  const storeOf = (path: string) => (/[\\/]global[\\/]/.test(path) ? 'user' : 'project');

  $effect(() => {
    topic = null;
    changed = null;
    problem = null;

    if (slug && scope && name) {
      memory.read(slug, scope, name).then((found) => {
        topic = found;
        draft = found.text;
      }, fail);
    }
  });

  async function save() {
    if (!slug || !scope || !name || !topic) {
      return;
    }

    saving = true;
    problem = null;

    try {
      const result = await memory.save(slug, scope, name, draft, topic.version);

      if (result.changed) {
        changed = result.changed;
      } else if (result.saved) {
        topic = result.saved;
        draft = result.saved.text;
        toasts.add({ level: 'done', text: `Saved ${name}`, cmd: '$ loadout workspace save · commits it to the workspace' });
        listing = await memory.topics(slug);
      }
    } catch (error) {
      fail(error);
    } finally {
      saving = false;
    }
  }

  /** Saves your edit over what is on disk now, which you have now seen. */
  function keepMine() {
    topic = changed;
    changed = null;
    void save();
  }

  function takeTheirs() {
    if (changed) {
      topic = changed;
      draft = changed.text;
      changed = null;
    }
  }

  const topicsIn = (key: string) => listing?.topics.filter((one) => one.scope === key) ?? [];
</script>

<div class="head"><div><h1>Memory</h1><span class="fact">what each project remembers, by topic</span></div></div>

<div class="memory">
  <aside class="memside" aria-label="Topics">
    {#if projects.length}
      {#key slug}
        <Picker label="Project" selected={Math.max(0, projects.findIndex((one) => one.slug === slug))} options={projects.map((one) => ({ name: one.slug, sub: one.name }))} onpick={(option) => router.go('memory', option.name)} />
      {/key}
    {/if}

    {#if listing}
      {#each STORES as store (store.key)}
        <section class="store" aria-labelledby={`st-${store.key}`}>
          <h2 class="ls-zone" id={`st-${store.key}`}>{store.label}</h2>
          <p class="calm small">{store.hint}</p>
          {#if !topicsIn(store.key).length}
            <p class="calm">Nothing recorded.</p>
          {:else}
            <ul class="slist">
              {#each topicsIn(store.key) as one (one.name)}
                <li>
                  <a class="srow" href={`#memory/${encodeURIComponent(slug ?? '')}/${store.key}/${encodeURIComponent(one.name)}`} aria-current={scope === store.key && name === one.name ? 'page' : undefined}>
                    <span class="t">{one.name}</span><span class="x">{one.description}</span>
                  </a>
                </li>
              {/each}
            </ul>
          {/if}
        </section>
      {/each}

      {#if listing.agent}
        <section class="store" aria-labelledby="st-agent">
          <h2 class="ls-zone" id="st-agent">Recorded by the agent, outside the workspace</h2>
          <p class="calm small">Shown, never written from here. Bring it in with:</p>
          <code class="ls-cmdline">$ {listing.agent.command}</code>
          {#if listing.agent.error}
            <p class="offline">{listing.agent.error}</p>
          {:else}
            <p class="calm small">
              {listing.agent.wouldImport?.length ?? 0} topic(s) would come in.{#if listing.agent.drifted?.length}
                {listing.agent.drifted.length} differ from the workspace's copy: {listing.agent.drifted.join(', ')}.{/if}
            </p>
          {/if}
        </section>
      {/if}
    {:else if slug}
      <p class="calm" role="status">Reading…</p>
    {/if}
  </aside>

  <section class="memmain" aria-label="Topic">
    {#if problem}<p class="offline" role="alert">{problem}</p>{/if}

    {#if changed}
      <div class="waitcard problem" role="alert">
        <p class="q">Somebody else changed this topic while you were editing</p>
        <span class="w">Nothing was saved. On the left is what is on disk now; on the right is your edit. Merge what you want into yours, then keep it, or take theirs and lose your edit.</span>
        <div class="compare">
          <label><span class="lab">On disk now</span><textarea name="on-disk" readonly rows="16" value={changed.text}></textarea></label>
          <label><span class="lab">Your edit</span><textarea name="your-edit" rows="16" bind:value={draft} spellcheck="false"></textarea></label>
        </div>
        <div class="ls-answers flush">
          <HaloButton label="Keep my edit, then save" kind="needs" size="small" cmd={replace} onpress={keepMine} />
          <HaloButton label="Take theirs" size="small" cmd="drops your edit · changes nothing" onpress={takeTheirs} />
        </div>
      </div>
    {:else if topic}
      <h2 class="topicname">{topic.name}</h2>
      <p class="calm">{topic.description}</p>
      <label class="editor"><span class="ls-sr">The topic's text</span><textarea name="topic-text" bind:value={draft} readonly={!editable} spellcheck="false" rows="24"></textarea></label>
      <p class="calm small">As stored, from <code class="ls-cmd">loadout memory show {topic.name} --project {slug}{scope && scope !== 'project' ? ` --scope ${scope}` : ''}</code>.</p>
      <div class="ls-answers flush">
        {#if editable}
          <HaloButton label={saving ? 'Saving…' : 'Save'} solid size="small" cmd={replace} disabledReason={!dirty ? 'nothing changed yet' : undefined} onpress={() => void save()} />
          {#if dirty}<HaloButton label="Discard changes" size="small" cmd="changes nothing" onpress={() => topic && (draft = topic.text)} />{/if}
        {:else if studio.watchOnly}
          <p class="calm small">Studio is only watching. Edit it in a terminal: <code class="ls-cmd">{replace}</code></p>
        {:else}
          <p class="calm small">This machine's memory is read-only here. Edit it in a terminal: <code class="ls-cmd">{replace}</code></p>
        {/if}
      </div>
      {#if topic && scope === 'project' && !studio.watchOnly}
        <div class="acts">
          <CommandAction path="memory delete" title="Delete this topic" preset={{ topic: topic.name, project: slug ?? '', '--yes': true }} fixed={['topic', 'project']} label="Delete it" />
        </div>
        <p class="calm small">A backup is taken first. Topics that link to it are named and left as they are.</p>
      {/if}
    {:else if slug}
      <h2 class="topicname">What {slug} remembers</h2>
      <p class="calm small">The topics on the left are <code class="ls-cmd">loadout memory list {slug}</code>. Choose one to read or edit it.</p>

      <form class="cmdform row" onsubmit={(event) => { event.preventDefault(); void find(); }}>
        <label class="fld"><span class="lab">Find the topics that answer</span><input name="memory-find" bind:value={query} autocomplete="off" /></label>
        <HaloButton label="Find" size="small" cmd={lineFor('memory find', [query.trim() || '<question>', '--project', slug])} disabledReason={query.trim() ? undefined : 'say what you are looking for'} onpress={() => void find()} />
      </form>
      {#if found}
        {#if found.length}
          <ul class="slist">
            {#each found as one (one.path)}
              <li><a class="srow" href={`#memory/${encodeURIComponent(slug)}/${storeOf(one.path)}/${encodeURIComponent(one.name)}`}><span class="t">{one.name}</span><span class="x">{one.description}</span></a></li>
            {/each}
          </ul>
        {:else}
          <p class="calm">Nothing matches. It matches words rather than meanings, so a topic that says the same thing differently will not come back.</p>
        {/if}
      {/if}

      <h3 class="ls-zone">Health</h3>
      {#if audit}
        <p class="calm">{audit.topics} topics; the audit says <strong>{audit.verdict.toLowerCase()}</strong>.</p>
        <ul class="findings">
          {#each audit.findings as finding, index (index)}
            <li class={finding.severity}><span class="sev">{finding.severity}</span> <span class="n">{finding.topic ?? finding.kind}</span> · {finding.detail}</li>
          {/each}
        </ul>
        <p class="calm small">From <code class="ls-cmd">loadout memory audit {slug}</code>.</p>
      {:else}
        <p class="calm" role="status">Checking…</p>
      {/if}

      <h3 class="ls-zone">Do</h3>
      <div class="acts">
        <CommandAction path="memory write" title="Record a fact" preset={{ '--project': slug }} fixed={['--project']} label="Record it" />
        <CommandAction path="memory audit" title="Clean up what the audit found" preset={{ project: slug, '--clean': true }} fixed={['project']} label="Clean it" />
        <CommandAction path="memory import" title="Bring in what the agent recorded outside the workspace" preset={{ project: slug }} fixed={['project']} label="Import" />
        <CommandAction path="memory compress" title="Move durable facts out of always-loaded instructions" preset={{ PROJECT: slug }} fixed={['PROJECT']} label="Compress" />
        <CommandAction path="memory reindex" title="Rebuild the index from the topics that exist" preset={{ project: slug }} fixed={['project']} label="Rebuild it" />
      </div>
      <p class="calm small">Clean, import and compress only report what they would do until you tick <code class="ls-cmd">--apply</code>.</p>
    {:else}
      <p class="calm">Choose a project.</p>
    {/if}
  </section>
</div>
