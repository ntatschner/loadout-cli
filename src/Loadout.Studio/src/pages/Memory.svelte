<script>
  import { memory, SignedOut } from '../lib/api.js';

  /**
   * @type {{
   *   slug: string | null,
   *   scope: string | null,
   *   name: string | null,
   *   signedOut: () => void,
   * }}
   */
  let { slug = null, scope = null, name = null, signedOut } = $props();

  const stores = [
    { key: 'project', label: 'This project', hint: 'Travels with the workspace.' },
    { key: 'user', label: 'Your work, any project', hint: 'Travels with the workspace.' },
    { key: 'machine', label: 'This machine only', hint: 'Never synced. Read-only here.' },
  ];

  let projects = $state(/** @type {{ slug: string, name: string }[]} */ ([]));
  let listing = $state(/** @type {any} */ (null));
  let topic = $state(/** @type {any} */ (null));
  let draft = $state('');
  let changed = $state(/** @type {any} */ (null));
  let problem = $state(/** @type {string | null} */ (null));
  let notice = $state(/** @type {string | null} */ (null));
  let saving = $state(false);

  let dirty = $derived(topic !== null && draft !== topic.text);
  let editable = $derived(topic !== null && topic.scope !== 'machine');

  function fail(error) {
    if (error instanceof SignedOut) {
      signedOut();
    } else {
      problem = error.message;
    }
  }

  $effect(() => {
    memory.projects().then((all) => {
      projects = all;

      if (!slug && all.length > 0) {
        location.hash = `#/memory/${encodeURIComponent(all[0].slug)}`;
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
    notice = null;
    problem = null;

    if (slug && scope && name) {
      memory.read(slug, scope, name).then((found) => {
        topic = found;
        draft = found.text;
      }, fail);
    }
  });

  function choose(event) {
    location.hash = `#/memory/${encodeURIComponent(event.currentTarget.value)}`;
  }

  async function save() {
    saving = true;
    problem = null;
    notice = null;

    try {
      const result = await memory.save(slug, scope, name, draft, topic.version);

      if (result.changed) {
        changed = result.changed;
      } else {
        topic = result.saved;
        draft = result.saved.text;
        changed = null;
        notice = 'Saved. Commit it to the workspace with: loadout workspace save';
        listing = await memory.topics(slug);
      }
    } catch (error) {
      fail(error);
    } finally {
      saving = false;
    }
  }

  /**
   * Saves your edit over what is on disk now, which you have now seen. If it
   * changed yet again meanwhile, the comparison comes back.
   */
  function keepMine() {
    topic = changed;
    changed = null;
    save();
  }

  /** Drops your edit for what is on disk now. */
  function takeTheirs() {
    topic = changed;
    draft = changed.text;
    changed = null;
  }

  function topicsIn(key) {
    return listing?.topics.filter((one) => one.scope === key) ?? [];
  }
</script>

<div class="page">
  <aside class="side">
    <h1>Memory</h1>

    <label>
      <span>Project</span>
      <select name="project" value={slug ?? ''} onchange={choose}>
        {#each projects as project (project.slug)}
          <option value={project.slug}>{project.name}</option>
        {/each}
      </select>
    </label>

    {#if listing}
      {#each stores as store (store.key)}
        <section class="store">
          <h2>{store.label}</h2>
          <p class="hint">{store.hint}</p>
          {#if topicsIn(store.key).length === 0}
            <p class="empty">Nothing recorded.</p>
          {:else}
            <ul>
              {#each topicsIn(store.key) as one (one.name)}
                <li>
                  <a
                    href={`#/memory/${encodeURIComponent(slug)}/${store.key}/${encodeURIComponent(one.name)}`}
                    aria-current={scope === store.key && name === one.name ? 'page' : undefined}
                  >
                    <span class="name">{one.name}</span>
                    <span class="description">{one.description}</span>
                  </a>
                </li>
              {/each}
            </ul>
          {/if}
        </section>
      {/each}

      {#if listing.agent}
        <section class="store agent">
          <h2>Recorded by the agent, outside the workspace</h2>
          <p class="hint">Shown, never written from here. Bring it in with:</p>
          <pre class="command">{listing.agent.command}</pre>
          {#if listing.agent.error}
            <p class="notice" role="alert">{listing.agent.error}</p>
          {:else}
            <p>
              {listing.agent.wouldImport.length} topic(s) would come in.
              {#if listing.agent.drifted.length > 0}
                <strong>{listing.agent.drifted.length} differ from the workspace's copy:</strong>
                {listing.agent.drifted.join(', ')}.
              {/if}
            </p>
          {/if}
        </section>
      {/if}
    {:else if slug}
      <p class="empty">Reading…</p>
    {/if}
  </aside>

  <section class="main" aria-label="Topic">
    {#if problem}
      <p class="notice" role="alert">{problem}</p>
    {/if}

    {#if changed}
      <div class="conflict" role="alert">
        <h2>Somebody else changed this topic while you were editing</h2>
        <p>
          Nothing was saved. On the left is what is on disk now; on the right is your edit. Merge what you want into
          yours, then keep it, or take theirs and lose your edit.
        </p>
        <div class="compare">
          <label>
            <span>On disk now</span>
            <textarea readonly rows="16" value={changed.text}></textarea>
          </label>
          <label>
            <span>Your edit</span>
            <textarea rows="16" bind:value={draft} spellcheck="false"></textarea>
          </label>
        </div>
        <div class="actions">
          <button type="button" class="primary" onclick={keepMine}>Keep my edit, then save</button>
          <button type="button" onclick={takeTheirs}>Take theirs</button>
        </div>
      </div>
    {:else if topic}
      <header class="topic">
        <h2>{topic.name}</h2>
        <p class="hint">{topic.description}</p>
      </header>
      <label class="editor">
        <span class="visually-hidden">The topic's text</span>
        <textarea
          name="text"
          bind:value={draft}
          readonly={!editable}
          spellcheck="false"
          rows="24"
        ></textarea>
      </label>
      <div class="actions">
        {#if editable}
          <button type="button" class="primary" onclick={save} disabled={!dirty || saving}>
            {saving ? 'Saving…' : 'Save'}
          </button>
          {#if dirty}
            <button type="button" onclick={() => (draft = topic.text)}>Discard changes</button>
          {/if}
        {:else}
          <p class="hint">This machine's memory is read-only here. Edit it with 'loadout memory replace'.</p>
        {/if}
      </div>
      {#if notice}
        <p class="saved" role="status">{notice}</p>
      {/if}
    {:else}
      <p class="empty">Choose a topic to read or edit it.</p>
    {/if}
  </section>
</div>

<style>
  .page {
    display: grid;
    grid-template-columns: minmax(16rem, 24rem) 1fr;
    gap: var(--space-4);
    min-height: 0;
    flex: 1;
  }

  @media (max-width: 48rem) {
    .page {
      grid-template-columns: 1fr;
    }
  }

  .side {
    overflow-y: auto;
    min-width: 0;
  }

  h1 {
    font-size: var(--size-title);
    margin: 0 0 var(--space-3);
  }

  h2 {
    font-size: var(--size-body);
    margin: 0;
  }

  label {
    display: grid;
    gap: var(--space-1);
    font-weight: 600;
  }

  select {
    font: inherit;
    min-height: 2.5rem;
    color: var(--colour-text);
    background: var(--colour-surface);
    border: 1px solid var(--colour-border);
    border-radius: var(--radius-small);
    padding: var(--space-1) var(--space-2);
  }

  .store {
    margin-top: var(--space-4);
  }

  .hint {
    margin: var(--space-1) 0 var(--space-2);
    color: var(--colour-text-muted);
    font-size: var(--size-small);
    font-weight: 400;
  }

  ul {
    list-style: none;
    margin: 0;
    padding: 0;
    display: grid;
    gap: var(--space-1);
  }

  li a {
    display: grid;
    gap: 2px;
    padding: var(--space-2);
    border-radius: var(--radius-small);
    border: 1px solid transparent;
    color: inherit;
    text-decoration: none;
  }

  li a:hover,
  li a[aria-current='page'] {
    border-color: var(--colour-accent);
    background: var(--colour-surface-raised);
  }

  .name {
    font-weight: 600;
    overflow-wrap: anywhere;
  }

  .description {
    color: var(--colour-text-muted);
    font-size: var(--size-small);
  }

  .main {
    display: flex;
    flex-direction: column;
    gap: var(--space-3);
    min-width: 0;
    min-height: 0;
  }

  .editor {
    flex: 1;
    display: flex;
  }

  textarea {
    flex: 1;
    width: 100%;
    font-family: var(--font-mono);
    font-size: var(--size-small);
    line-height: 1.5;
    color: var(--colour-text);
    background: var(--colour-surface-sunken);
    border: 1px solid var(--colour-border);
    border-radius: var(--radius-small);
    padding: var(--space-3);
    resize: vertical;
  }

  textarea[readonly] {
    color: var(--colour-text-muted);
  }

  .actions {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-2);
    align-items: center;
  }

  .compare {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--space-3);
    margin: var(--space-3) 0;
  }

  @media (max-width: 64rem) {
    .compare {
      grid-template-columns: 1fr;
    }
  }

  .conflict {
    border: 2px solid var(--colour-warn);
    border-radius: var(--radius-medium);
    padding: var(--space-4);
  }

  .command {
    font-family: var(--font-mono);
    background: var(--colour-surface-sunken);
    padding: var(--space-2);
    border-radius: var(--radius-small);
    overflow-x: auto;
  }

  .empty {
    color: var(--colour-text-muted);
  }

  .saved {
    color: var(--colour-ok);
  }

  .notice {
    color: var(--colour-danger);
    background: var(--colour-danger-surface);
    border: 1px solid var(--colour-danger);
    border-radius: var(--radius-small);
    padding: var(--space-2) var(--space-3);
    overflow-wrap: anywhere;
    white-space: pre-wrap;
  }
</style>
