<script>
  import { onMount } from 'svelte';
  import { sessions, SignedOut } from '../lib/api.js';

  /** @type {{ signedOut: () => void }} */
  let { signedOut } = $props();

  let list = $state(/** @type {any[] | null} */ (null));

  let running = $derived(list?.filter((one) => !one.ended).length ?? 0);

  async function refresh() {
    try {
      list = await sessions.list();
    } catch (error) {
      if (error instanceof SignedOut) {
        signedOut();
      }
    }
  }

  onMount(() => {
    refresh();
    const timer = setInterval(refresh, 5000);

    return () => clearInterval(timer);
  });
</script>

<h1>Home</h1>

<div class="cards">
  <a class="card" href="#/sessions">
    <h2>Sessions</h2>
    {#if list === null}
      <p>Reading…</p>
    {:else}
      <p><strong>{running}</strong> running{list.length > running ? `, ${list.length - running} ended` : ''}</p>
    {/if}
  </a>
</div>

<style>
  h1 {
    font-size: var(--size-title);
    margin: 0 0 var(--space-4);
  }

  .cards {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(14rem, 1fr));
    gap: var(--space-4);
  }

  .card {
    display: block;
    padding: var(--space-4);
    border: 1px solid var(--colour-border-subtle);
    border-radius: var(--radius-medium);
    background: var(--colour-surface-raised);
    box-shadow: var(--shadow-raised);
    color: inherit;
    text-decoration: none;
  }

  .card:hover {
    border-color: var(--colour-accent);
  }

  h2 {
    margin: 0 0 var(--space-2);
    font-size: var(--size-body);
    color: var(--colour-accent-text);
  }

  p {
    margin: 0;
  }
</style>
