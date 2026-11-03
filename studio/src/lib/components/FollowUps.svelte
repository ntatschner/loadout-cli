<script lang="ts">
  // What a run's final report left for later. Nothing reaches the task list
  // until somebody says so: each waits here to be made a task on the run's
  // project, or dismissed so it is not offered again.
  import HaloButton from './HaloButton.svelte';
  import type { RunFollowUp } from '../api';
  import { runLines } from '../teamsAct';

  interface Props {
    run: string;
    items: RunFollowUp[];
    disabledReason?: string;
    /** Makes one a task, by its id; resolves false where it was not taken. */
    onaccept?: (id: string) => Promise<boolean | void> | boolean | void;
    /** Dismisses one, with the reason given or none; resolves false where it was not taken. */
    ondismiss?: (id: string, why: string) => Promise<boolean | void> | boolean | void;
  }

  let { run, items, disabledReason = undefined, onaccept = undefined, ondismiss = undefined }: Props = $props();

  const waiting = $derived(items.filter((one) => one.state === 'proposed').length);
  let sending = $state<string | null>(null);
  // The one whose reason box is open: dismissing asks why first, and an
  // empty box is a dismissal with no reason.
  let dismissing = $state<string | null>(null);
  let why = $state('');

  async function act(id: string, work: () => Promise<boolean | void> | boolean | void) {
    if (sending !== null) {
      return;
    }

    sending = id;

    try {
      if ((await work()) !== false && dismissing === id) {
        dismissing = null;
        why = '';
      }
    } finally {
      sending = null;
    }
  }

  function startDismissing(id: string) {
    dismissing = id;
    why = '';
  }
</script>

<section class="ls-gate followups" role="group" aria-labelledby="ls-followups">
  <span class="who">left for later · {waiting ? `${waiting} of ${items.length} waiting` : 'all decided'}</span>
  <p class="q" id="ls-followups">It left these for later. Make each a task, or dismiss it.</p>
  <ol class="readings">
    {#each items as one (one.id)}
      <li>
        <span class="c">{one.id} · {one.title}</span>
        {#if one.why}<span class="m">{one.why}</span>{/if}
        {#if one.state === 'accepted'}
          <span class="m">✓ made the task {one.task ?? 'a task'}{one.by ? ` by ${one.by}` : ''}</span>
        {:else if one.state === 'dismissed'}
          <span class="m">dismissed{one.by ? ` by ${one.by}` : ''}{one.reason ? `: ${one.reason}` : ''}</span>
        {:else if dismissing === one.id}
          <label class="m">
            Why not, if you want it kept
            <input type="text" bind:value={why} placeholder="optional" />
          </label>
          <span class="acts">
            <HaloButton
              label="Dismiss it"
              size="small"
              cmd={runLines.dismiss(run, one.id, why.trim())}
              disabledReason={disabledReason ?? (sending !== null ? 'deciding one' : undefined)}
              onpress={() => void act(one.id, () => ondismiss?.(one.id, why.trim()))}
            />
            <HaloButton label="Keep it waiting" size="small" onpress={() => (dismissing = null)} />
          </span>
        {:else}
          <span class="acts">
            <HaloButton
              label="Make it a task"
              size="small"
              cmd={runLines.accept(run, one.id)}
              disabledReason={disabledReason ?? (sending !== null ? 'deciding one' : undefined)}
              onpress={() => void act(one.id, () => onaccept?.(one.id))}
            />
            <HaloButton
              label="Dismiss…"
              size="small"
              disabledReason={disabledReason ?? (sending !== null ? 'deciding one' : undefined)}
              onpress={() => startDismissing(one.id)}
            />
          </span>
        {/if}
      </li>
    {/each}
  </ol>
  <p class="ls-note">In a terminal: <code>$ loadout team follow-ups list {run}</code></p>
</section>
