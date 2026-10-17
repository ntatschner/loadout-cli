<script lang="ts">
  // Markdown drawn as elements in Studio's type, from the parsed tree: nothing
  // from the file is ever HTML, and only http and https links are live.
  import { markdown, type Run } from '../files';

  let { source }: { source: string } = $props();

  const blocks = $derived(markdown(source));
</script>

{#snippet runs(list: Run[])}
  {#each list as run, index (index)}
    {#if run.t === 'text'}{run.text}{:else if run.t === 'code'}<code>{run.text}</code>{:else if run.t === 'strong'}<strong>{@render runs(run.runs)}</strong>{:else if run.t === 'em'}<em>{@render runs(run.runs)}</em>{:else if run.t === 'link'}<a href={run.href} target="_blank" rel="noopener noreferrer">{@render runs(run.runs)}</a>{:else}<span>{@render runs(run.runs)} (<code>{run.href}</code>)</span>{/if}
  {/each}
{/snippet}

<div class="ls-md">
  {#each blocks as block, index (index)}
    {#if block.t === 'p'}
      <p>{@render runs(block.runs)}</p>
    {:else if block.t === 'h'}
      <svelte:element this={`h${block.level}`}>{@render runs(block.runs)}</svelte:element>
    {:else if block.t === 'hr'}
      <hr />
    {:else if block.t === 'pre'}
      <pre><code>{block.text}</code></pre>
    {:else if block.t === 'list'}
      <svelte:element this={block.ordered ? 'ol' : 'ul'}>
        {#each block.items as item, n (n)}<li>{@render runs(item)}</li>{/each}
      </svelte:element>
    {:else if block.t === 'quote'}
      <blockquote><p>{@render runs(block.runs)}</p></blockquote>
    {:else if block.t === 'table'}
      <table>
        <thead><tr>{#each block.head as cell, n (n)}<th scope="col">{@render runs(cell)}</th>{/each}</tr></thead>
        <tbody>
          {#each block.rows as row, r (r)}<tr>{#each row as cell, n (n)}<td>{@render runs(cell)}</td>{/each}</tr>{/each}
        </tbody>
      </table>
    {/if}
  {/each}
</div>
