<script lang="ts">
  // A CSV or TSV as a table, its first row the head, and a column whose every
  // cell is a number right-aligned.
  import { parseCsv } from '../files';

  let { text, separator = ',' }: { text: string; separator?: string } = $props();

  const parsed = $derived(parseCsv(text, separator));
  const head = $derived(parsed[0] ?? []);
  const rows = $derived(parsed.slice(1));
  const numeric = $derived(head.map((_, k) => rows.length > 0 && rows.every((row) => !row[k] || /^-?[\d.,]+%?$/.test(row[k]))));
</script>

<table class="ls-csv">
  <caption class="ls-note">{rows.length} {rows.length === 1 ? 'row' : 'rows'} · {head.length} columns</caption>
  <thead><tr>{#each head as cell, n (n)}<th scope="col">{cell}</th>{/each}</tr></thead>
  <tbody>
    {#each rows as row, r (r)}
      <tr>{#each head as _, k (k)}<td class={numeric[k] ? 'r' : undefined}>{row[k] ?? ''}</td>{/each}</tr>
    {/each}
  </tbody>
</table>
