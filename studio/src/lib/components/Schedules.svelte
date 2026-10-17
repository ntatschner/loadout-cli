<script lang="ts">
  // Runs that start without you, and whether the daemon will fire them: a
  // schedule on a machine with no daemon running is a promise nothing keeps,
  // so it is said first.
  import Command from './Command.svelte';
  import HaloButton from './HaloButton.svelte';

  interface Item {
    name: string;
    team: string;
    goal: string;
    /** "every 24h", "at 09:00", "on commit". */
    when: string;
    next?: string | null;
    autonomy?: string | null;
    usd?: string | null;
    /** Why it will not fire, where it is held. */
    held?: string | null;
  }

  interface Props {
    daemon: { running: boolean; paused?: boolean | null };
    items: Item[];
    disabledReason?: string;
    onremove?: (name: string) => Promise<boolean | void> | boolean | void;
  }

  let { daemon, items, disabledReason = undefined, onremove = undefined }: Props = $props();

  let gone = $state<string[]>([]);
  const shown = $derived(items.filter((item) => !gone.includes(item.name)));

  async function remove(name: string) {
    if ((await onremove?.(name)) !== false) {
      gone = [...gone, name];
    }
  }
</script>

<div class="ls-sched">
  <p class="ls-note" class:needs={!daemon.running}>
    {#if daemon.running && daemon.paused}
      The daemon is held: no schedule fires until it is resumed. <Command command="$ loadout daemon resume" />
    {:else if daemon.running}
      The daemon is running and fires these.
    {:else}
      The daemon is not running, so none of these will fire. <Command command="$ loadout daemon" />
    {/if}
  </p>
  {#if !shown.length}
    <p class="ls-note">Nothing is scheduled on this machine.</p>
  {:else}
    <ul aria-label="Runs on a schedule">
      {#each shown as item (item.name)}
        <li>
          <div class="t">
            <b>{item.name}</b>
            <span>{item.team} · {item.goal}</span>
            <em>{[item.when, item.next ? `next ${item.next}` : null, item.autonomy, item.usd ? `$${item.usd} a run` : null, item.held].filter(Boolean).join(' · ')}</em>
          </div>
          <HaloButton label="Stop it" size="small" cmd={`$ loadout team schedule remove ${item.name}`} {disabledReason} onpress={() => void remove(item.name)} />
        </li>
      {/each}
    </ul>
  {/if}
</div>
