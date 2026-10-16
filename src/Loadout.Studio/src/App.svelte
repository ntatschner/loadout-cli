<script>
  import Home from './pages/Home.svelte';
  import Sessions from './pages/Sessions.svelte';
  import Memory from './pages/Memory.svelte';

  // Routes live in the hash, so the daemon serves one page for all of them
  // and a reload or an installed app window opens where it was.
  let hash = $state(location.hash);
  let signedOut = $state(false);

  $effect(() => {
    const changed = () => (hash = location.hash);
    window.addEventListener('hashchange', changed);

    return () => window.removeEventListener('hashchange', changed);
  });

  let route = $derived.by(() => {
    const parts = hash.replace(/^#\/?/, '').split('/').filter(Boolean).map(decodeURIComponent);

    return { page: parts[0] ?? 'home', id: parts[1] ?? null, rest: parts.slice(1) };
  });

  const pages = [
    { key: 'home', label: 'Home' },
    { key: 'sessions', label: 'Sessions' },
    { key: 'memory', label: 'Memory' },
  ];
</script>

<a class="skip" href="#content">Skip to content</a>

<header class="top">
  <span class="brand">Loadout Studio</span>
  <a
    class="pill preview"
    href="https://github.com/ntatschner/loadout-cli/issues"
    target="_blank"
    rel="noreferrer noopener"
    title="Studio is a preview. Report a problem on GitHub."
  >Preview</a>
  <nav aria-label="Studio">
    <ul>
      {#each pages as page (page.key)}
        <li>
          <a href={`#/${page.key}`} aria-current={route.page === page.key ? 'page' : undefined}>{page.label}</a>
        </li>
      {/each}
    </ul>
  </nav>
</header>

<main id="content" tabindex="-1">
  {#if signedOut}
    <h1>Not signed in</h1>
    <p>This window has no sign-in, or it was signed out. Open Studio again from a terminal on this machine:</p>
    <pre class="command">loadout dashboard --open --studio</pre>
  {:else if route.page === 'sessions'}
    <Sessions open={route.id} signedOut={() => (signedOut = true)} />
  {:else if route.page === 'memory'}
    <Memory
      slug={route.rest[0] ?? null}
      scope={route.rest[1] ?? null}
      name={route.rest[2] ?? null}
      signedOut={() => (signedOut = true)}
    />
  {:else}
    <Home signedOut={() => (signedOut = true)} />
  {/if}
</main>

<style>
  .top {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--space-3);
    padding: var(--space-2) var(--space-4);
    background: var(--colour-surface-raised);
    border-bottom: 1px solid var(--colour-border-subtle);
  }

  .brand {
    font-weight: 700;
  }

  nav ul {
    display: flex;
    gap: var(--space-1);
    list-style: none;
    margin: 0;
    padding: 0;
  }

  nav a {
    display: inline-block;
    padding: var(--space-1) var(--space-3);
    border-radius: var(--radius-small);
    color: var(--colour-text);
    text-decoration: none;
  }

  nav a[aria-current='page'] {
    background: var(--colour-accent);
    color: var(--colour-on-accent);
  }

  .preview {
    text-decoration: none;
  }

  main {
    display: flex;
    flex-direction: column;
    flex: 1;
    min-height: 0;
    padding: var(--space-4);
  }

  main:focus {
    outline: none;
  }

  .command {
    font-family: var(--font-mono);
    background: var(--colour-surface-sunken);
    padding: var(--space-3);
    border-radius: var(--radius-small);
    overflow-x: auto;
  }

  .skip {
    position: absolute;
    left: var(--space-2);
    top: -10rem;
  }

  .skip:focus {
    top: var(--space-2);
    z-index: 10;
    background: var(--colour-surface);
    padding: var(--space-2);
  }
</style>
