<script lang="ts">
  // A team's nodes as the lead's line and its branches: the first node is the
  // lead, and each node lists the ones it briefs beneath it. A node briefed by
  // two is drawn once, under the first.
  interface TeamNode {
    name: string;
    role: string;
    mode?: string | null;
    deliverable?: string | null;
    delegates?: string[];
    instances?: number;
    worktree?: boolean;
  }

  let { nodes }: { nodes: TeamNode[] } = $props();

  interface Drawn {
    node: TeamNode;
    lead: boolean;
    children: Drawn[];
  }

  /** The tree, each node once: what the reference builds as it draws. */
  const tree = $derived.by(() => {
    const by = new Map(nodes.map((node) => [node.name, node]));
    const seen = new Set<string>();

    function grow(node: TeamNode, lead: boolean): Drawn {
      seen.add(node.name);
      const kids = (node.delegates ?? []).filter((name) => by.has(name) && !seen.has(name));
      // Marked before descending, so a node two parents name is drawn under the first only.
      kids.forEach((name) => seen.add(name));

      return { node, lead, children: kids.map((name) => grow(by.get(name)!, false)) };
    }

    return nodes[0] ? grow(nodes[0], true) : null;
  });

  const facts = (node: TeamNode) =>
    [node.role, node.mode, (node.instances ?? 1) > 1 ? `up to ${node.instances} at once` : null, node.worktree ? 'each in its own worktree' : null].filter(Boolean).join(' · ');
</script>

{#snippet item(drawn: Drawn)}
  <li class={drawn.lead ? 'lead' : undefined}>
    <div class="nd">
      <span class="dot" aria-hidden="true"></span>
      <div>
        <b>{drawn.node.name}</b>
        <span>{facts(drawn.node)}</span>
        {#if drawn.node.deliverable}<span>Hands back: {drawn.node.deliverable}</span>{/if}
      </div>
    </div>
    {#if drawn.children.length}
      <ul aria-label={`Briefed by ${drawn.node.name}`}>
        {#each drawn.children as child (child.node.name)}{@render item(child)}{/each}
      </ul>
    {/if}
  </li>
{/snippet}

<div class="ls-tnodes">
  {#if tree}
    <ul aria-label="Nodes">{@render item(tree)}</ul>
  {:else}
    <p>No nodes.</p>
  {/if}
</div>
