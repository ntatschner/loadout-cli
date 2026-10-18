<script lang="ts">
  // ActionDrawer: one command a page offers that is not its main action.
  // Closed, it is one line: what it does, `loadout <path>`, and its effect,
  // which comes from Studio's coverage list, never from the page. Opened, it
  // is the command's own form. Pages name the command by its path, so the
  // coverage test can see which page does which command.
  import CommandForm from './CommandForm.svelte';
  import Glyph from './Glyph.svelte';
  import { studio } from '../state.svelte';
  import type { Effect } from '../api';

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
    /** Says more than the command's own description, when the page has more to say. */
    why?: string;
    /** A line under the button. */
    note?: string;
    /** The button acknowledges a loss: forgetting, deleting. */
    danger?: boolean;
  }

  let { path, title, preset = {}, fixed = [], label = 'Run', open = false, onlaunch = undefined, why = undefined, note = undefined, danger = false }: Props = $props();

  const command = $derived(studio.command(path));
  // Opened by the page's open, then by the person: a page reading again does not shut it.
  let opened = $state<boolean | null>(null);
  const isOpen = $derived(opened ?? open);
  const id = `dr-${Math.random().toString(36).slice(2, 8)}`;

  const TAG: Record<Effect, [string, string]> = {
    reads: ['reads only', ''],
    changes: ['changes something', 'accent'],
    session: ['starts a session', 'needs'],
    terminalOnly: ['terminal only', ''],
  };

  const tag = $derived(command ? TAG[command.reach.effect] : null);

  $effect(() => {
    void studio.loadCatalogue().catch(() => undefined);
  });
</script>

<section class="ls-drawer" data-open={isOpen ? 'true' : 'false'} data-effect={command?.reach.effect ?? ''}>
  <button type="button" class="top" aria-expanded={isOpen ? 'true' : 'false'} aria-controls={id} onclick={() => (opened = !isOpen)}>
    <Glyph name="chev" cls="chev" />
    <span class="t">{title}</span>
    <code>loadout {path}</code>
    {#if tag}<span class={`ls-tag ${tag[1]}`}>{#if command?.reach.effect === 'terminalOnly'}<Glyph name="lock" />{/if}{tag[0]}</span>{/if}
  </button>
  <div class="body" {id} role="group" aria-label={title}>
    {#if command}
      <CommandForm {command} {preset} {fixed} {label} {onlaunch} {why} {danger} drawer />
    {:else}
      <p class="calm" role="status">Reading what it takes…</p>
    {/if}
    {#if note}<p class="note">{note}</p>{/if}
  </div>
</section>
