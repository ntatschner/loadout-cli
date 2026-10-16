<script>
  import { onMount } from 'svelte';
  import { sessions, SignedOut } from '../lib/api.js';
  import { size } from '../lib/format.js';

  /** @type {{ signedOut: () => void }} */
  let { signedOut } = $props();

  let list = $state(/** @type {any[] | null} */ (null));

  let running = $derived(list?.filter((one) => !one.ended).length ?? 0);
  let memory = $derived(
    list?.filter((one) => !one.ended && one.memory !== null).reduce((total, one) => total + one.memory, 0) ?? 0,
  );
  let measured = $derived(list?.some((one) => !one.ended && one.memory !== null) ?? false);

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
      {#if running > 0}
        <p class="memory">
          {measured ? `About ${size(memory)} of memory` : 'Memory not measured'}
          <span class="hint">for the sessions and the agents they started</span>
        </p>
      {/if}
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

  .memory {
    margin-top: var(--space-2);
  }

  .hint {
    display: block;
    color: var(--colour-text-muted);
    font-size: var(--size-small);
  }
</style>
