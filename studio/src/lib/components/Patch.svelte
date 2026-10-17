<script lang="ts">
  // What a node changed: its files first, then the patch, and opening a pull
  // request for it, the one thing on the page that reaches a remote, so it
  // asks first and offers to say what it would do instead.
  import Dialog from './Dialog.svelte';
  import HaloButton from './HaloButton.svelte';
  import Switch from './Switch.svelte';
  import { quoted } from '../commands';

  type DiffLine = [' ' | '+' | '-' | '@', string];

  interface Props {
    run: string;
    node: string;
    branch?: string | null;
    base?: string | null;
    goal?: string | null;
    /** [path, added, removed]. */
    files?: [string, number, number][];
    diff?: DiffLine[];
    /** Recorded before the commit each branch started at was written down. */
    unmeasured?: boolean;
    pr?: boolean;
    disabledReason?: string;
    /** Opens it, or with dry: true says what it would do; resolves to what was said, or null where it failed. */
    onpr?: (o: { draft: boolean; title: string; dry: boolean }) => Promise<string | null> | string | null;
  }

  let { run, node, branch = null, base = null, goal = null, files = [], diff = [], unmeasured = false, pr = true, disabledReason = undefined, onpr = undefined }: Props = $props();

  let asking = $state(false);
  let draft = $state(true);
  let title = $state('');
  let said = $state('');

  const added = $derived(files.reduce((sum, file) => sum + file[1], 0));
  const removed = $derived(files.reduce((sum, file) => sum + file[2], 0));
  const CLASS: Record<string, string> = { '+': 'add', '-': 'del', '@': 'hunk', ' ': 'ctx' };

  /** `$ loadout team pr <run> --node <node> --draft --title "…" --dry-run`. */
  export function prLine(dry: boolean) {
    return `$ loadout team pr ${run} --node ${node}${draft ? ' --draft' : ''}${title.trim() ? ` --title ${quoted(title.trim())}` : ''}${dry ? ' --dry-run' : ''}`;
  }

  async function open(dry: boolean) {
    const answer = await onpr?.({ draft, title: title.trim(), dry });

    said = answer ?? (dry ? `✓ Dry run: it would push ${branch ?? 'the branch'} and open the pull request. Nothing was pushed.` : `✓ Pushed ${branch ?? 'the branch'} and opened a ${draft ? 'draft ' : ''}pull request.`);
  }
</script>

<section class="ls-patch" aria-label={`What ${node} changed`}>
  <div class="head">
    <b>{node}</b>
    {#if branch}<span class="ls-mono">{branch}</span>{/if}
    {#if base}<span class="ls-mono">measured from {base}</span>{/if}
  </div>
  {#if unmeasured}
    <p class="none">This run was recorded before the commit each branch started at was written down, so its patch cannot be shown. A diff measured from the wrong place would look like one.</p>
  {:else}
    <p class="ls-note">{files.length} {files.length === 1 ? 'file' : 'files'} · {added} added · {removed} removed</p>
    <ul class="files">
      {#each files as [path, plus, minus] (path)}
        <li><span class="a">+{plus}</span><span class="d">−{minus}</span><span class="p">{path}</span></li>
      {/each}
    </ul>
    {#if diff.length}
      <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
      <pre class="diff" tabindex="0" aria-label="The patch">{#each diff as [mark, text], index (index)}<span class={CLASS[mark] ?? 'ctx'}>{mark === '@' ? '' : mark === '-' ? '−' : mark}{text}</span>{/each}</pre>
    {/if}
    {#if pr}
      <div class="ls-answers">
        <HaloButton label="Open a pull request" size="small" glyph="↗" {disabledReason} cmd={`$ loadout team pr ${run} --node ${node} --draft`} onpress={() => (asking = true)} />
      </div>
      <p class="ls-said" role="status">{said}</p>
    {/if}
  {/if}
</section>

{#if asking}
  <Dialog
    title={`Open a pull request for ${node}?`}
    why={`This is the one thing on the page that reaches a remote. It pushes ${branch ?? 'the node’s branch'} to origin and opens the pull request with gh, using the GitHub login you already have.`}
    actions={[
      { label: 'Push and open it', kind: 'needs', solid: true, cmd: 'runs the command above', onpress: () => void open(false) },
      { label: 'Say what it would do', cmd: 'the same, with --dry-run · pushes nothing', onpress: () => void open(true) },
    ]}
    onclose={() => (asking = false)}
  >
    <div class="prform">
      <Switch label="Open it as a draft" sub="--draft" bind:checked={draft} />
      <div class="ls-field wide">
        <label class="lab" for={`pr-title-${node}`}>Its title</label>
        <input id={`pr-title-${node}`} name="pr-title" class="ls-text" type="text" placeholder={goal ?? 'The run’s goal'} autocomplete="off" bind:value={title} />
        <span class="hint">The run’s goal unless you give one.</span>
      </div>
      <code class="ls-cmdline">{prLine(false)}</code>
    </div>
  </Dialog>
{/if}
