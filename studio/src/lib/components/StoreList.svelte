<script lang="ts">
  // A project's memory in its three stores, each with what it means, and
  // what the agent recorded outside the workspace, which is shown here and
  // never written from here.
  interface Props {
    stores: { key: string; label: string; hint?: string; readOnly?: boolean; topics: { name: string; description?: string; facts?: number }[] }[];
    picked?: { store: string; name: string } | null;
    href: (store: string, name: string) => string;
    agent?: { command: string; wouldImport?: string[]; drifted?: string[]; error?: string } | null;
  }

  let { stores, picked = null, href, agent = null }: Props = $props();
</script>

<nav class="ls-stores" aria-label="Topics">
  {#each stores as store (store.key)}
    <section aria-labelledby={`st-${store.key}`}>
      <h3 id={`st-${store.key}`}>{store.label}{#if store.readOnly}<span class="ls-tag">read-only here</span>{/if}</h3>
      {#if store.hint}<p class="hint">{store.hint}</p>{/if}
      {#if !store.topics.length}
        <p class="empty">Nothing recorded.</p>
      {:else}
        <ul>
          {#each store.topics as topic (topic.name)}
            <li>
              <a href={href(store.key, topic.name)} aria-current={picked?.store === store.key && picked?.name === topic.name ? 'page' : undefined}>
                <span class="t">{topic.name}</span>
                <span class="x">{topic.description ?? ''}</span>
                {#if topic.facts !== undefined}<span class="n">{topic.facts} fact{topic.facts === 1 ? '' : 's'}</span>{/if}
              </a>
            </li>
          {/each}
        </ul>
      {/if}
    </section>
  {/each}

  {#if agent}
    <section class="agent" aria-labelledby="st-agent">
      <h3 id="st-agent">Recorded by the agent, outside the workspace</h3>
      <p>Shown, never written from here. Bring it in with:</p>
      <code>$ {agent.command}</code>
      {#if agent.error}
        <p class="needs">{agent.error}</p>
      {:else}
        <p>{agent.wouldImport?.length ?? 0} topic{(agent.wouldImport?.length ?? 0) === 1 ? '' : 's'} would come in.</p>
        {#if agent.drifted?.length}<p class="needs">{agent.drifted.length} differ{agent.drifted.length === 1 ? 's' : ''} from the workspace's copy: {agent.drifted.join(', ')}.</p>{/if}
      {/if}
    </section>
  {/if}
</nav>
