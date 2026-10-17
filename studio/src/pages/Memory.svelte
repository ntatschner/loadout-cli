<script lang="ts">
  // What each project remembers, by store and topic, read and edited in
  // place. A save carries the version it was read at, so a topic somebody
  // else changed meanwhile is shown beside the edit rather than overwritten.
  import HaloButton from '../lib/components/HaloButton.svelte';
  import Picker from '../lib/components/Picker.svelte';
  import { memory, SignedOut, type Topic } from '../lib/api';
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
          <label><span class="lab">On disk now</span><textarea readonly rows="16" value={changed.text}></textarea></label>
          <label><span class="lab">Your edit</span><textarea rows="16" bind:value={draft} spellcheck="false"></textarea></label>
        </div>
        <div class="ls-answers flush">
          <HaloButton label="Keep my edit, then save" kind="needs" size="small" cmd={replace} onpress={keepMine} />
          <HaloButton label="Take theirs" size="small" cmd="drops your edit · changes nothing" onpress={takeTheirs} />
        </div>
      </div>
    {:else if topic}
      <h2 class="topicname">{topic.name}</h2>
      <p class="calm">{topic.description}</p>
      <label class="editor"><span class="ls-sr">The topic's text</span><textarea bind:value={draft} readonly={!editable} spellcheck="false" rows="24"></textarea></label>
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
    {:else}
      <p class="calm">Choose a topic to read or edit it.</p>
    {/if}
  </section>
</div>
