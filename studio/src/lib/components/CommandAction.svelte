<script lang="ts">
  // One thing a page offers to do, folded until it is wanted: a title, the
  // command it runs, and that command's own form when it is opened. Pages
  // name the command by its path, so the coverage test can see which page
  // does which command.
  import CommandForm from './CommandForm.svelte';
  import { studio } from '../state.svelte';

  interface Props {
    /** The command, as the catalogue names it. */
    path: string;
    /** What it does, as the page would say it. */
    title: string;
    preset?: Record<string, string | boolean>;
    fixed?: string[];
    label?: string;
    open?: boolean;
    onlaunch?: () => void;
  }

  let { path, title, preset = {}, fixed = [], label = 'Run', open = false, onlaunch = undefined }: Props = $props();

  const command = $derived(studio.command(path));

  $effect(() => {
    void studio.loadCatalogue().catch(() => undefined);
  });
</script>

<details class="act" {open}>
  <summary><span class="t">{title}</span> <code class="ls-cmd">loadout {path}</code></summary>
  {#if command}
    <CommandForm {command} {preset} {fixed} {label} {onlaunch} />
  {:else}
    <p class="calm" role="status">Reading what it takes…</p>
  {/if}
</details>
