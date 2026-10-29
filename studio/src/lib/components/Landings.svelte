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
    /** When a squash-merge was asked for, or null when nobody has. */
    mergeRequested?: string | null;
    gone: boolean;
  }

  /** The words for 'loadout pr merge', leaving out a body nobody wrote. */
  export function mergeWords(one: Pick<LandingRead, 'project' | 'branch'>, subject: string, body: string): string[] {
    // Joined with '=' so a message that starts with a dash is still read as the value.
    const words = [one.branch, '--project', one.project, `--subject=${subject.trim()}`];

    return body.trim() ? [...words, `--body=${body.trim()}`] : words;
  }
</script>

<script lang="ts">
  import HaloButton from './HaloButton.svelte';

  // The branches waiting to land: a queued run's own branch with its pull
  // request, and each node branch its gate did not merge, pending. Each can
  // be opened, marked ready, or squash-merged with a message the person
  // writes, through the same commands as the terminal.
  interface Props {
    landings: LandingRead[];
    /** Runs a command and refreshes the page; false when it did not go through. */
    act?: (path: string, words: string[], done: string) => Promise<boolean | void> | boolean | void;
    /** When set, nothing can be pressed, and this says why. */
    disabledReason?: string;
    /** The clock ages are counted against, for tests. */
    now?: Date;
  }

  let { landings, act = undefined, disabledReason = undefined, now = new Date() }: Props = $props();

  // Kept here rather than on the row, so a refresh of the list every few
  // seconds does not throw away a message half written.
  let merging = $state<string | null>(null);
  let subject = $state('');
  let body = $state('');

  const keyOf = (one: LandingRead) => one.project + ' ' + one.branch;
  const at = (one: LandingRead) => [one.branch, '--project', one.project];

  function waited(created: string): string {
    const hours = (now.getTime() - new Date(created).getTime()) / 3_600_000;

    if (hours < 1) {
      return 'under an hour';
    }

    const [count, unit] = hours < 24 ? [Math.floor(hours), 'hour'] : [Math.floor(hours / 24), 'day'];

    return count === 1 ? `1 ${unit}` : `${count} ${unit}s`;
  }

  const from = (one: LandingRead) => (one.node ? `${one.node} in run ${one.run}` : one.run ? `run ${one.run}` : 'no run');

  function startMerge(one: LandingRead) {
    merging = keyOf(one);
    subject = '';
    body = '';
  }

  async function merge(one: LandingRead) {
    if ((await act?.('pr merge', mergeWords(one, subject, body), `${one.branch} merges once its checks pass`)) !== false) {
      merging = null;
    }
  }

  const subjectReason = () =>
    !subject.trim() ? 'Write the squash commit’s subject' : subject.includes('\n') ? 'The subject is one line' : undefined;
</script>

{#if landings.length}
  <ul class="landings">
    {#each landings as one, index (keyOf(one))}
      <li class="landing">
        <span class="lstate ls-chip">{one.state}</span>
        <code class="lbranch">{one.branch}</code>
        {#if one.gone}<span class="lgone">gone from the repository</span>{/if}
        <p class="calm small">{one.project}, {from(one)}{one.task ? `, task ${one.task}` : ''}, waiting {waited(one.created)}</p>
        {#if one.pullRequest}<p class="small"><a href={one.pullRequest} target="_blank" rel="noreferrer">{one.pullRequest}</a></p>{/if}
        {#if one.mergeRequested}<p class="calm small">Merges once its required checks pass.</p>{/if}
        {#if act}
          <div class="lacts">
            {#if one.state === 'pending' && !one.gone}
              <HaloButton label="Open as a draft" size="small" cmd={`$ loadout pr open ${one.branch} --draft`} {disabledReason} onpress={() => void act('pr open', [...at(one), '--draft'], `${one.branch} has a draft pull request`)} />
              <HaloButton label="Open for review" size="small" cmd={`$ loadout pr open ${one.branch}`} {disabledReason} onpress={() => void act('pr open', at(one), `${one.branch} has a pull request`)} />
            {:else if one.state === 'draft' && one.pullRequest}
              <HaloButton label="Mark ready" size="small" cmd={`$ loadout pr ready ${one.branch}`} {disabledReason} onpress={() => void act('pr ready', at(one), `${one.branch} is ready for review`)} />
            {:else if one.state === 'ready' && one.pullRequest && !one.mergeRequested}
              <HaloButton label={merging === keyOf(one) ? 'Not now' : 'Merge…'} size="small" cmd={merging === keyOf(one) ? undefined : `opens the squash message for ${one.branch}`} {disabledReason} onpress={() => (merging === keyOf(one) ? (merging = null) : startMerge(one))} />
            {/if}
          </div>
          {#if merging === keyOf(one) && one.state === 'ready'}
            <form class="lmerge" aria-label={`Squash-merge ${one.branch}`} onsubmit={(event) => { event.preventDefault(); void merge(one); }}>
              <div class="ls-field">
                <label class="lab" for={`lm-subject-${index}`}>Subject</label>
                <input id={`lm-subject-${index}`} class="ls-text" bind:value={subject} placeholder="One line, as git log will show it" />
              </div>
              <div class="ls-field">
                <label class="lab" for={`lm-body-${index}`}>Body</label>
                <textarea id={`lm-body-${index}`} class="ls-text" rows="4" bind:value={body} placeholder="Left empty, the commit has no body"></textarea>
              </div>
              <HaloButton label="Squash-merge when checks pass" solid cmd={`$ loadout pr merge ${one.branch} --subject …`} disabledReason={disabledReason ?? subjectReason()} onpress={() => void merge(one)} />
            </form>
          {/if}
        {/if}
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

  .lacts {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-2);
    margin-top: var(--space-1);
  }

  .lmerge {
    display: grid;
    gap: var(--space-2);
    margin-top: var(--space-2);
    max-width: 60ch;
  }
</style>
