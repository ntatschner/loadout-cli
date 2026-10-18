<script lang="ts">
  // What a run delivered: its files in a tree, by node and commit or by
  // folder, beside a viewer that previews Markdown, code, CSV, JSON and
  // images, shows each file's part of its node's patch, and downloads one
  // file or all of them. It only reads, so its buttons show no command; each
  // file shows the terminal line that reads it instead.
  import { untrack } from 'svelte';
  import CodeView from './CodeView.svelte';
  import CsvTable from './CsvTable.svelte';
  import HaloButton from './HaloButton.svelte';
  import Markdown from './Markdown.svelte';
  import { css } from '../css';
  import { baseName, extension, fileKind, sizeOf, type OutboxRead } from '../files';


  interface Entry {
    key: string;
    path: string;
    node: string;
    loose: boolean;
    change?: 'added' | 'changed' | 'removed';
    commit?: string;
    kind?: string;
    bytes?: number | null;
    note?: string | null;
    /** A reported file's path from the repository, for showing. */
    shown?: string;
  }

  interface Props {
    run: string;
    repository: string | null;
    files: { path: string; change: 'added' | 'changed' | 'removed'; commit: string; node: string; bytes?: number }[];
    loose?: { path: string; kind: string; bytes?: number | null; node: string; note?: string | null }[];
    missing?: string[];
    by?: 'node' | 'folder';
    picked?: string | null;
    read: (entry: Entry, mode: 'file' | 'change') => Promise<OutboxRead>;
    ondownload?: (entry: Entry) => void;
    ondownloadall?: () => void;
    downloadAllReason?: string;
  }

  let { run, repository, files, loose = [], missing = [], by = 'node', picked = null, read, ondownload = undefined, ondownloadall = undefined, downloadAllReason = undefined }: Props = $props();

  /** A reported file's path as the reader knows it: from the repository, where it is inside it. */
  function shownPath(path: string): string {
    const root = (repository ?? '').replace(/\\/g, '/').replace(/\/$/, '').toLowerCase();
    const plain = path.replace(/\\/g, '/');

    return root && plain.toLowerCase().startsWith(`${root}/`) ? plain.slice(root.length + 1) : path;
  }

  const all = $derived<Entry[]>([
    ...files.map((file) => ({ ...file, key: `${file.commit}:${file.path}`, loose: false })),
    // Keyed by the path the run reported, which is what the file route is asked for.
    ...loose.map((file) => ({ ...file, key: `loose:${file.path}`, loose: true, bytes: file.bytes ?? null, shown: shownPath(file.path) })),
  ]);
  const commits = $derived([...new Set(files.map((file) => file.commit))]);

  // Starts as the page asks, then is the reader's own choice.
  let grouping = $state<'node' | 'folder'>(untrack(() => by));
  let filter = $state('');
  let chosen = $state<string | null>(null);
  let mode = $state<'file' | 'change'>('file');
  let raw = $state(false);
  let reading = $state(false);
  let closed = $state<Record<string, boolean>>({});
  let focus = $state<string | null>(null);
  let said = $state('');
  let tree = $state<HTMLUListElement | undefined>();

  const current = $derived(all.find((entry) => entry.key === (chosen ?? picked ?? all[0]?.key)) ?? null);

  /* ---------- the tree ---------- */

  type Item = { group: true; key: string; label: string; sub: string | null; level: number; children: Item[] } | { group: false; key: string; entry: Entry; label: string; level: number };

  function folders(list: (Entry & { rel: string })[], level: number, prefix: string): Item[] {
    const dirs = new Map<string, (Entry & { rel: string })[]>();
    const here: (Entry & { rel: string })[] = [];

    for (const entry of list) {
      const rest = entry.rel.slice(prefix.length);
      const cut = rest.indexOf('/');

      if (cut < 0) {
        here.push(entry);
      } else {
        const dir = rest.slice(0, cut);
        dirs.set(dir, [...(dirs.get(dir) ?? []), entry]);
      }
    }

    return [
      ...[...dirs.keys()].sort().map((dir): Item => ({
        group: true,
        key: `dir:${prefix}${dir}`,
        label: `${dir}/`,
        sub: String(dirs.get(dir)!.length),
        level,
        children: folders(dirs.get(dir)!, level + 1, `${prefix}${dir}/`),
      })),
      ...here.sort((a, b) => (a.rel < b.rel ? -1 : 1)).map((entry): Item => ({ group: false, key: entry.key, entry, label: baseName(entry.path), level })),
    ];
  }

  const shown = $derived(all.filter((entry) => !filter.trim() || entry.path.toLowerCase().includes(filter.trim().toLowerCase())));

  const items = $derived.by((): Item[] => {
    if (grouping === 'folder') {
      return folders(shown.map((entry) => ({ ...entry, rel: (entry.shown ?? entry.path).replace(/\\/g, '/').replace(/^[A-Za-z]:\//, '') })), 0, '');
    }

    const nodes = [...new Set(shown.map((entry) => entry.node))];

    return nodes.map((node): Item => {
      const mine = shown.filter((entry) => entry.node === node);
      const groups = [...new Set(mine.map((entry) => (entry.loose ? 'not committed' : entry.commit!)))];

      return {
        group: true,
        key: `node:${node}`,
        label: node,
        sub: `${mine.length} ${mine.length === 1 ? 'file' : 'files'}`,
        level: 0,
        children: groups.map((commit): Item => ({
          group: true,
          key: `commit:${node}${commit}`,
          label: commit,
          sub: commit === 'not committed' ? 'on disk' : null,
          level: 1,
          children: mine.filter((entry) => (entry.loose ? 'not committed' : entry.commit) === commit).map((entry): Item => ({ group: false, key: entry.key, entry, label: entry.shown ?? entry.path, level: 2 })),
        })),
      };
    });
  });

  /** A group is open unless it was closed, and always while a filter is set. */
  const isOpen = (key: string) => !closed[key] || !!filter.trim();

  /** What can be reached with the arrow keys, in order. */
  const visible = $derived.by(() => {
    const out: Item[] = [];
    const walk = (list: Item[]) => {
      for (const item of list) {
        out.push(item);

        if (item.group && isOpen(item.key)) {
          walk(item.children);
        }
      }
    };

    walk(items);

    return out;
  });

  const parentOf = (key: string): Item | null => {
    let found: Item | null = null;
    const walk = (list: Item[], parent: Item | null) => {
      for (const item of list) {
        if (item.key === key) {
          found = parent;
        } else if (item.group) {
          walk(item.children, item);
        }
      }
    };

    walk(items, null);

    return found;
  };

  const tabStop = $derived(focus && visible.some((item) => item.key === focus) ? focus : (current?.key && visible.some((item) => item.key === current.key) ? current.key : visible[0]?.key ?? null));

  function moveTo(key: string | undefined) {
    if (!key) {
      return;
    }

    focus = key;
    queueMicrotask(() => tree?.querySelector<HTMLElement>(`[data-item="${CSS.escape(key)}"]`)?.focus());
  }

  function toggle(key: string, to?: boolean) {
    closed[key] = !(to ?? !isOpen(key));
  }

  function pick(key: string) {
    chosen = key;
    focus = key;
    mode = 'file';
    raw = false;
    reading = true;
  }

  function keys(event: KeyboardEvent) {
    const key = (event.target as HTMLElement).closest<HTMLElement>('[data-item]')?.dataset.item;

    if (!key) {
      return;
    }

    const index = visible.findIndex((item) => item.key === key);
    const item = visible[index];

    switch (event.key) {
      case 'ArrowDown':
        moveTo(visible[index + 1]?.key);
        break;
      case 'ArrowUp':
        moveTo(visible[index - 1]?.key);
        break;
      case 'Home':
        moveTo(visible[0]?.key);
        break;
      case 'End':
        moveTo(visible[visible.length - 1]?.key);
        break;
      case 'ArrowRight':
        if (item?.group) {
          if (isOpen(item.key)) {
            moveTo(visible[index + 1]?.key);
          } else {
            toggle(item.key, true);
          }
        }

        break;
      case 'ArrowLeft':
        if (item?.group && isOpen(item.key)) {
          toggle(item.key, false);
        } else {
          moveTo(parentOf(key)?.key);
        }

        break;
      case 'Enter':
      case ' ':
        if (item?.group) {
          toggle(item.key);
        } else if (item) {
          pick(item.key);
        }

        break;
      default:
        return;
    }

    event.preventDefault();
  }

  function back() {
    reading = false;
    moveTo(current?.key);
  }

  /* ---------- the viewer ---------- */

  let got = $state<{ key: string; mode: string; read?: OutboxRead; error?: string } | null>(null);

  $effect(() => {
    const entry = current;
    const how = mode;

    if (!entry) {
      got = null;

      return;
    }

    let live = true;
    got = null;
    read(entry, how).then(
      (answer) => live && (got = { key: entry.key, mode: how, read: answer }),
      (error: Error) => live && (got = { key: entry.key, mode: how, error: error.message }),
    );

    return () => {
      live = false;
    };
  });

  const kind = $derived(current ? fileKind(current.path) : 'text');
  const pretty = (text: string) => {
    try {
      return JSON.stringify(JSON.parse(text), null, 2);
    } catch {
      return text;
    }
  };

  const facts = $derived(
    !current
      ? ''
      : current.loose
        ? [current.node, 'reported, not committed', current.bytes === null ? 'gone' : sizeOf(current.bytes)].join(' · ') + (current.note ? ` · ${current.note}` : '')
        : [current.node, `${current.change} in ${current.commit}`, current.bytes !== undefined && current.bytes !== null ? sizeOf(current.bytes) : null].filter(Boolean).join(' · '),
  );

  const terminalLine = $derived(
    !current ? '' : current.loose ? `In a terminal: $ cat ${current.path}` : `In a terminal: $ git -C ${repository ?? '<repo>'} show ${current.change === 'removed' ? `${current.commit}~1` : current.commit}:${current.path}`,
  );

  function download() {
    if (current) {
      said = `✓ Downloading ${baseName(current.path)}${current.loose ? ', as it is on disk now.' : `, as ${current.commit} left it.`}`;
      ondownload?.(current);
    }
  }

  function downloadAll() {
    said = `✓ Downloading ${run}-outbox.zip: ${all.filter((entry) => entry.bytes !== null).length} files, each as its commit left it.`;
    ondownloadall?.();
  }

  async function copyPath() {
    if (!current) {
      return;
    }

    const full = `${repository ? `${repository.replace(/[\\/]$/, '')}/` : ''}${current.path}`;

    try {
      await navigator.clipboard.writeText(full);
    } catch {
      // The path is on screen either way.
    }

    said = `✓ Copied ${full}`;
  }
</script>

{#snippet chip(entry: Entry)}
  {#if entry.loose}
    {#if entry.bytes === null}<span class="ch gone">gone</span>{:else}<span class="ch loose" title="reported, not committed">{entry.kind ?? 'file'}</span>{/if}
  {:else}
    <span class={`ch ${entry.change}`}>{entry.change}</span>
  {/if}
{/snippet}

{#snippet branch(list: Item[])}
  {#each list as item (item.key)}
    {#if item.group}
      <li role="treeitem" tabindex={tabStop === item.key ? 0 : -1} aria-expanded={isOpen(item.key) ? 'true' : 'false'} aria-level={item.level + 1} aria-selected="false" data-item={item.key}>
        <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
        <div class="ti" use:css={{ '--lvl': item.level }} onclick={() => toggle(item.key)}><span class="tw" aria-hidden="true"></span><span class="grpname">{item.label}{#if item.sub}<em> · {item.sub}</em>{/if}</span><span></span></div>
        <ul role="group" hidden={!isOpen(item.key)}>{@render branch(item.children)}</ul>
      </li>
    {:else}
      <li role="treeitem" tabindex={tabStop === item.key ? 0 : -1} aria-selected={current?.key === item.key ? 'true' : 'false'} aria-level={item.level + 1} data-item={item.key}>
        <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
        <div class="ti" use:css={{ '--lvl': item.level }} title={item.entry.path} onclick={() => pick(item.key)}><span class="fi" aria-hidden="true"></span><span class="nm">{item.label}</span>{@render chip(item.entry)}</div>
      </li>
    {/if}
  {/each}
{/snippet}

<section class="ls-outbox" class:reading aria-label="What it delivered">
  <div class="ohead">
    <div class="sum">
      <b>{files.length} {files.length === 1 ? 'file' : 'files'} from {commits.length} {commits.length === 1 ? 'commit' : 'commits'}{loose.length ? ` · ${loose.length} not committed` : ''}</b>
      <span>{repository ?? 'repository not recorded'} · $ loadout team outbox {run}</span>
    </div>
    <HaloButton label="Download all" size="small" glyph="↓" cmd={`${run}-outbox.zip · reads only`} disabledReason={all.length ? downloadAllReason : 'nothing was delivered'} onpress={downloadAll} />
  </div>
  <p class="ls-said" role="status">{said}</p>

  {#if !repository && !all.length}
    <p class="notice">Every node of this run had its own worktree, and they have been cleared away, so where it worked cannot be told. Nothing can be listed.</p>
  {:else}
    <div class="split">
      <div class="side">
        <div class="grp" role="group" aria-label="Group the files">
          <button type="button" aria-pressed={grouping === 'node' ? 'true' : 'false'} onclick={() => (grouping = 'node')}>By node</button>
          <button type="button" aria-pressed={grouping === 'folder' ? 'true' : 'false'} onclick={() => (grouping = 'folder')}>By folder</button>
        </div>
        <input class="ls-text find" type="search" name="outbox-find" placeholder="Find a file" aria-label="Find a file" autocomplete="off" bind:value={filter} />
        <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
        <ul bind:this={tree} role="tree" aria-label="Delivered files" onkeydown={keys}>
          {@render branch(items)}
          {#each missing as commit (commit)}
            <li role="treeitem" tabindex="-1" class="missing" aria-level="1" aria-disabled="true" aria-selected="false"><div class="ti"><span class="fi" aria-hidden="true"></span><span class="nm">commit {commit}</span><span class="ch gone">no longer here</span></div></li>
          {/each}
          {#if !shown.length}
            <li role="none"><p class="ls-note tree-empty">{filter.trim() ? `No delivered file matches “${filter.trim()}”.` : 'This run delivered no files.'}</p></li>
          {/if}
        </ul>
      </div>

      <div class="view">
        <div class="vhead">
          <button type="button" class="ls-halo small back" onclick={back}><span class="ring" aria-hidden="true"></span>The files</button>
          <div class="path">{#if current}<b>{current.shown ?? current.path}</b><span>{facts}</span>{/if}</div>
          <div class="vbar" role="group" aria-label="How to show it">
            {#if current && !current.loose && current.change !== 'removed'}
              <button type="button" aria-pressed={mode === 'file' ? 'true' : 'false'} onclick={() => (mode = 'file')}>The file</button>
              <button type="button" aria-pressed={mode === 'change' ? 'true' : 'false'} onclick={() => (mode = 'change')}>The change</button>
            {/if}
            {#if current && mode === 'file' && kind === 'markdown'}
              <button type="button" aria-pressed="false" onclick={() => (raw = !raw)}>{raw ? 'Rendered' : 'Source'}</button>
            {/if}
          </div>
        </div>
        <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
        <div class="vbody" tabindex="0" role="region" aria-label="File contents">
          {#if !current}
            <p class="empty">Pick a file to see it.</p>
          {:else if !got || got.key !== current.key || got.mode !== mode}
            <p class="empty" role="status">Reading…</p>
          {:else if got.error}
            <p class="notice bad">✕ It could not be read: {got.error}</p>
          {:else if got.read}
            {@const r = got.read}
            {#if r.redacted}<p class="notice needs">{r.redacted} {r.redacted === 1 ? 'line' : 'lines'} that looked like credentials were taken out before it was shown.</p>{/if}
            {#if current.loose && current.bytes === null}
              <p class="notice bad">Nothing is there now. {current.node} reported it, but it has since gone.</p>
            {:else if r.unchecked}
              <p class="notice">It could not be checked for credentials in time, so it is not shown. Download it to read it.</p>
            {:else}
              {#if current.change === 'removed'}<p class="notice">{current.commit} removed it. This is how it was just before.</p>{/if}
              {#if mode === 'change'}
                {#if r.diff?.length}<CodeView diff={r.diff} />{:else}<p class="empty">No change to show for this file.</p>{/if}
              {:else if kind === 'image'}
                <div class="ls-img"><div class="chk"><img src={r.url} alt={`The image ${baseName(current.path)}, as delivered`} /></div><span>{[r.width && r.height ? `${r.width} × ${r.height}` : null, sizeOf(current.bytes)].filter(Boolean).join(' · ')}</span></div>
              {:else if kind === 'binary' || r.binary}
                <p class="empty">A binary file ({sizeOf(current.bytes) || 'size unknown'}), so it is not shown here. Download it to open it.</p>
              {:else}
                {#if r.truncated}<p class="notice">It is {sizeOf(current.bytes) || 'long'}, so only its first {r.truncated.toLocaleString('en-GB')} lines are shown. Download it for the rest.</p>{/if}
                {#if kind === 'markdown' && !raw}
                  <Markdown source={r.text ?? ''} />
                {:else if kind === 'csv'}
                  <CsvTable text={r.text ?? ''} separator={extension(current.path) === 'tsv' ? '\t' : ','} />
                {:else if kind === 'json'}
                  <CodeView text={pretty(r.text ?? '')} lang="json" />
                {:else}
                  <CodeView text={r.text ?? ''} lang={extension(current.path)} />
                {/if}
              {/if}
            {/if}
          {/if}
        </div>
        {#if current}
          <div class="vfoot">
            <HaloButton label="Download" size="small" glyph="↓" disabledReason={current.bytes === null ? 'nothing is there now' : current.change === 'removed' ? 'removed by this commit' : undefined} onpress={download} />
            <HaloButton label="Copy path" size="small" onpress={() => void copyPath()} />
            <code class="ls-cmd">{terminalLine}</code>
          </div>
        {/if}
      </div>
    </div>
  {/if}
</section>
