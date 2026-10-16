<script>
  import { onMount } from 'svelte';
  import { sessions, SignedOut } from '../lib/api.js';
  import Terminal from '../lib/Terminal.svelte';

  /** @type {{ open: string | null, signedOut: () => void }} */
  let { open = null, signedOut } = $props();

  let list = $state(/** @type {any[]} */ ([]));
  let problem = $state(/** @type {string | null} */ (null));
  let project = $state('');
  let agent = $state('');
  let starting = $state(false);

  let shown = $derived(list.find((one) => one.id === open) ?? null);

  async function refresh() {
    try {
      list = await sessions.list();
      problem = null;
    } catch (error) {
      if (error instanceof SignedOut) {
        signedOut();
      } else {
        problem = error.message;
      }
    }
  }

  async function start(event) {
    event.preventDefault();
    starting = true;

    try {
      const made = await sessions.start({ project: project.trim(), agent: agent.trim() || undefined });
      project = '';
      agent = '';
      await refresh();
      location.hash = `#/sessions/${made.id}`;
    } catch (error) {
      if (error instanceof SignedOut) {
        signedOut();
      } else {
        problem = error.message;
      }
    } finally {
      starting = false;
    }
  }

  async function end(session) {
    const running = !session.ended;

    if (running && !confirm(`End ${session.title}? The agent stops at once.`)) {
      return;
    }

    try {
      await sessions.end(session.id);

      if (open === session.id) {
        location.hash = '#/sessions';
      }

      await refresh();
    } catch (error) {
      problem = error.message;
    }
  }

  onMount(() => {
    refresh();
    const timer = setInterval(refresh, 3000);

    return () => clearInterval(timer);
  });
</script>

<div class="page">
  <aside class="side">
    <h1>Sessions</h1>

    <form onsubmit={start}>
      <label>
        <span>Project</span>
        <input name="project" bind:value={project} required autocomplete="off" spellcheck="false" />
      </label>
      <label>
        <span>Agent <span class="hint">(optional)</span></span>
        <input name="agent" bind:value={agent} autocomplete="off" spellcheck="false" />
      </label>
      <button type="submit" class="primary" disabled={starting || project.trim().length === 0}>
        {starting ? 'Starting…' : 'Start a session'}
      </button>
    </form>

    {#if problem}
      <p class="notice" role="alert">{problem}</p>
    {/if}

    {#if list.length === 0}
      <p class="empty">Nothing is running here. Sessions you start stay running when you close this window, until the daemon stops.</p>
    {:else}
      <ul>
        {#each list as session (session.id)}
          <li class:current={session.id === open}>
            <a href={`#/sessions/${session.id}`} aria-current={session.id === open ? 'page' : undefined}>
              <span class="title">{session.title}</span>
              <span class="pill" data-state={session.ended ? 'done' : 'running'}>
                {session.ended ? `ended${session.exitCode === null ? '' : ` (${session.exitCode})`}` : 'running'}
              </span>
            </a>
            <button type="button" class="quiet" onclick={() => end(session)}>
              {session.ended ? 'Remove' : 'End'}<span class="visually-hidden"> {session.title}</span>
            </button>
          </li>
        {/each}
      </ul>
    {/if}
  </aside>

  <section class="main">
    {#if shown}
      {#key shown.id}
        <Terminal session={shown} />
      {/key}
    {:else if open}
      <p class="empty">That session is not here any more.</p>
    {:else}
      <p class="empty">Choose a session, or start one.</p>
    {/if}
  </section>
</div>

<style>
  .page {
    display: grid;
    grid-template-columns: minmax(16rem, 20rem) 1fr;
    gap: var(--space-4);
    min-height: 0;
    flex: 1;
  }

  @media (max-width: 48rem) {
    .page {
      grid-template-columns: 1fr;
    }
  }

  h1 {
    font-size: var(--size-title);
    margin: 0 0 var(--space-3);
  }

  form {
    display: grid;
    gap: var(--space-3);
    margin-bottom: var(--space-4);
  }

  label {
    display: grid;
    gap: var(--space-1);
    font-weight: 600;
  }

  .hint {
    font-weight: 400;
    color: var(--colour-text-muted);
  }

  ul {
    list-style: none;
    margin: 0;
    padding: 0;
    display: grid;
    gap: var(--space-2);
  }

  li {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    padding: var(--space-2);
    border: 1px solid var(--colour-border-subtle);
    border-radius: var(--radius-medium);
    background: var(--colour-surface-raised);
  }

  li.current {
    border-color: var(--colour-accent);
  }

  li a {
    flex: 1;
    display: grid;
    gap: var(--space-1);
    color: inherit;
    text-decoration: none;
    min-width: 0;
  }

  .title {
    overflow-wrap: anywhere;
    font-weight: 600;
  }

  .main {
    display: flex;
    min-height: 0;
    min-width: 0;
  }

  .empty {
    color: var(--colour-text-muted);
  }

  .notice {
    color: var(--colour-danger);
    background: var(--colour-danger-surface);
    border: 1px solid var(--colour-danger);
    border-radius: var(--radius-small);
    padding: var(--space-2) var(--space-3);
    overflow-wrap: anywhere;
  }
</style>
