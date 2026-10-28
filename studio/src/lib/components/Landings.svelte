<script lang="ts" module>
  /** One branch waiting to land, as 'loadout pr list --json' gives it. */
  export interface LandingRead {
    project: string;
    branch: string;
    state: 'pending' | 'draft' | 'ready' | 'merged' | 'abandoned';
    team: string;
    run: string;
    node: string;
    task: string;
    pullRequest: string;
    why: string;
    created: string;
    gone: boolean;
  }
</script>

<script lang="ts">
  // The branches waiting to land: a queued run's own branch with its pull
  // request, and each node branch its gate did not merge, pending. Read only
  // for now; opening, readying and merging come with the commands that do them.
  interface Props {
    landings: LandingRead[];
    /** The clock ages are counted against, for tests. */
    now?: Date;
  }

  let { landings, now = new Date() }: Props = $props();

  function waited(created: string): string {
    const hours = (now.getTime() - new Date(created).getTime()) / 3_600_000;

    if (hours < 1) {
      return 'under an hour';
    }

    const [count, unit] = hours < 24 ? [Math.floor(hours), 'hour'] : [Math.floor(hours / 24), 'day'];

    return count === 1 ? `1 ${unit}` : `${count} ${unit}s`;
  }

  const from = (one: LandingRead) => (one.node ? `${one.node} in run ${one.run}` : one.run ? `run ${one.run}` : 'no run');
</script>

{#if landings.length}
  <ul class="landings">
    {#each landings as one (one.project + ' ' + one.branch)}
      <li class="landing">
        <span class="lstate ls-chip">{one.state}</span>
        <code class="lbranch">{one.branch}</code>
        {#if one.gone}<span class="lgone">gone from the repository</span>{/if}
        <p class="calm small">{one.project}, {from(one)}{one.task ? `, task ${one.task}` : ''}, waiting {waited(one.created)}</p>
        {#if one.pullRequest}<p class="small"><a href={one.pullRequest} target="_blank" rel="noreferrer">{one.pullRequest}</a></p>{/if}
      </li>
    {/each}
  </ul>
{:else}
  <p class="calm">Nothing is waiting to land.</p>
{/if}
<p class="calm small">From <code class="ls-cmd">$ loadout pr list</code>.</p>

<style>
  .landings {
    list-style: none;
    margin: 0;
    padding: 0;
    display: grid;
    gap: var(--space-2);
  }

  .landing p {
    margin: 2px 0 0;
  }

  .lbranch {
    margin-inline-start: 6px;
  }

  .lgone {
    margin-inline-start: 6px;
    color: var(--needs-text);
  }
</style>
