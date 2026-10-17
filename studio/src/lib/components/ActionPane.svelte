<script lang="ts">
  // The picked thing's actions, numbered 1 to 9, each with its key and the
  // command it runs. A danger action asks for its key a second time: the
  // first press arms it for three seconds, and Escape keeps it. There is no
  // dialog. What was run flashes once, and says so.
  import { tick, untrack } from 'svelte';
  import type { PureAction } from '../pure';

  interface Props {
    actions: PureAction[];
    /** Whose actions these are: only a change of that disarms, not the list being read again. */
    owner?: string | null;
    emptyText?: string;
    legend?: boolean;
  }

  let { actions, owner = null, emptyText = 'Nothing to do here.', legend = true }: Props = $props();

  let armed = $state<number | null>(null);
  let said = $state('');
  let refused = $state(false);
  let acked = $state<number | null>(null);
  let timer: ReturnType<typeof setTimeout> | null = null;

  const shown = $derived(actions.slice(0, 9));

  export function isArmed(): boolean {
    return armed !== null;
  }

  export function disarm() {
    if (armed === null) {
      return;
    }

    if (timer) {
      clearTimeout(timer);
      timer = null;
    }

    armed = null;

    if (refused) {
      said = '';
      refused = false;
    }
  }

  export async function run(index: number) {
    const action = shown[index];

    if (!action) {
      return;
    }

    if (action.disabledReason || !action.run) {
      refused = true;
      said = `${action.label}: ${action.disabledReason ?? action.cmd ?? 'nothing to run here'}`;

      return;
    }

    if (action.kind === 'danger' && armed !== index) {
      disarm();
      armed = index;
      refused = true;
      said = `Press ${index + 1} again, or Escape to keep it.`;
      timer = setTimeout(disarm, 3000);

      return;
    }

    disarm();
    // The flash plays again on every run, so the class comes off and back on.
    acked = null;
    await tick();
    acked = index;
    const result = await action.run();
    refused = false;
    said = `✓ ${typeof result === 'string' ? result : action.label}`;
  }

  // A different thing picked is a different set of actions: nothing stays
  // armed across it. The same thing read again by a poll keeps it armed.
  // Untracked, as disarm reads the armed state, and arming must not set it off.
  $effect(() => {
    void owner;
    untrack(disarm);
  });
</script>

<div class="ls-apane">
  {#each shown as action, index (index)}
    <button
      type="button"
      class={`act ${action.kind ?? ''}`}
      class:arming={armed === index}
      class:ls-ack={acked === index}
      aria-keyshortcuts={String(index + 1)}
      aria-disabled={action.disabledReason ? 'true' : undefined}
      onclick={() => void run(index)}
    >
      <kbd class="ls-kbd">{index + 1}</kbd>
      <span class="l">{armed === index ? `Press ${index + 1} again to ${action.label.toLowerCase()}` : action.label}</span>
      <code>{action.disabledReason ?? action.cmd ?? ''}</code>
    </button>
  {:else}
    <p class="empty">{emptyText}</p>
  {/each}
  <p class={refused ? 'said no' : 'said'} role="status">{said}</p>
  {#if legend}
    <div class="keys" aria-label="Keys">
      <span><kbd class="ls-kbd">J</kbd><kbd class="ls-kbd">K</kbd></span><span>move in the list</span>
      <span><kbd class="ls-kbd">Enter</kbd></span><span>open it</span>
      <span><kbd class="ls-kbd">1</kbd>–<kbd class="ls-kbd">9</kbd></span><span>run an action</span>
      <span><kbd class="ls-kbd">/</kbd></span><span>filter the list</span>
      <span><kbd class="ls-kbd">:</kbd></span><span>the command bar</span>
      <span><kbd class="ls-kbd">G</kbd></span><span>then a letter: go</span>
      <span><kbd class="ls-kbd">F6</kbd></span><span>the next pane</span>
      <span><kbd class="ls-kbd">?</kbd></span><span>every key</span>
    </div>
  {/if}
</div>
