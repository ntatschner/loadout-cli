<script lang="ts">
  // Code with line numbers, in the terminal palette. Each line is tokens
  // drawn as text, never markup from the file. A unified diff marks its
  // added, removed and hunk lines.
  import { codeLines, codeTokens } from '../files';

  interface Props {
    text?: string;
    lang?: string;
    limit?: number;
    /** A diff instead: [mark, text]. */
    diff?: [' ' | '+' | '-' | '@', string][] | null;
  }

  let { text = '', lang = '', limit = undefined, diff = null }: Props = $props();

  const MARK: Record<string, string> = { '+': 'add', '-': 'del', '@': 'hunk', ' ': '' };
  const rows = $derived(diff ? diff.map(([mark, line]) => ({ cls: MARK[mark] ?? '', hunk: mark === '@', line })) : codeLines(text, limit).map((line) => ({ cls: '', hunk: false, line })));
</script>

<pre class="ls-code" class:diffv={!!diff}>{#each rows as row, index (index)}<span class={`l ${row.cls}`.trim()}>{#if row.hunk}{row.line}{:else}{#each codeTokens(row.line, diff ? '' : lang) as token, n (n)}{#if token.cls}<span class={token.cls}>{token.text}</span>{:else}{token.text}{/if}{/each}{/if}</span>{/each}</pre>
