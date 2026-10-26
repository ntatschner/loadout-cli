<script lang="ts">
  // What a run handed to you because only you could do it: a push, a CI run
  // or a check by hand. Each is ticked off here once it is done, which the
  // journal keeps as review evidence in your name. The last tick judges the
  // run again, with no turn for the lead.
  import HaloButton from './HaloButton.svelte';
  import { runLines } from '../teamsAct';

  interface Item {
    criterion: string;
    needs: string;
    todo: string;
    checkedBy: string | null;
  }

  interface Props {
    run: string;
    items: Item[];
    branches?: string[];
    disabledReason?: string;
    /** Ticks one off, by its number; resolves false where it was not taken. */
    oncheck?: (number: number) => Promise<boolean | void> | boolean | void;
  }

  let { run, items, branches = [], disabledReason = undefined, oncheck = undefined }: Props = $props();

  const NEEDS: Record<string, string> = { 'needs-push': 'a push', 'needs-ci': 'a CI run', 'needs-person': 'your check' };
  const open = $derived(items.filter((one) => !one.checkedBy).length);
  let sending = $state<number | null>(null);

  async function press(number: number) {
    if (sending !== null) {
      return;
    }

    sending = number;

    try {
      await oncheck?.(number);
    } finally {
      sending = null;
    }
  }
</script>

<section class="ls-gate handover" role="group" aria-labelledby="ls-handover">
  <span class="who">handed over · {open ? `${open} of ${items.length} still yours` : 'all checked'}</span>
  <p class="q" id="ls-handover">It handed these over: only you can do them.</p>
  <ol class="readings">
    {#each items as one, index (index)}
      <li>
        <span class="c">{one.criterion} <span class="ls-chip">needs {NEEDS[one.needs] ?? one.needs}</span></span>
        <span class="m">{one.todo}</span>
        {#if one.checkedBy}
          <span class="m">✓ checked by {one.checkedBy}</span>
        {:else}
          <HaloButton
            label="Done: tick it off"
            size="small"
            cmd={runLines.check(run, index + 1) + (open === 1 ? ' · the last one, so it is judged again' : '')}
            disabledReason={disabledReason ?? (sending !== null ? 'ticking one off' : undefined)}
            onpress={() => void press(index + 1)}
          />
        {/if}
      </li>
    {/each}
  </ol>
  {#if branches.length}<p class="ls-note">The work is on {branches.join(', ')}.</p>{/if}
</section>
