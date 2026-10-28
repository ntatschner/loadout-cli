<script lang="ts">
  // The bin: removed runs and teams, waiting before they go for good. Put one
  // back, empty it (the one thing here with no way back, so it names what it
  // deletes and asks), or clear out runs, said back as a sentence first.
  import Dialog from './Dialog.svelte';
  import HaloButton from './HaloButton.svelte';
  import Picker from './Picker.svelte';
  import { pruneCommand, pruneSentence, type Prune } from '../teams';

  interface Entry {
    kind: 'run' | 'team';
    name: string;
    what?: string | null;
    removed: string;
    left: number;
    size?: string | null;
  }

  interface Props {
    keptDays?: number;
    entries: Entry[];
    disabledReason?: string;
    /** Each resolves false where it was not done. */
    onrestore?: (entry: Entry) => Promise<boolean | void> | boolean | void;
    onempty?: () => Promise<boolean | void> | boolean | void;
    onprune?: (prune: Prune, dry: boolean) => Promise<string | void> | string | void;
  }

  let { keptDays = 30, entries, disabledReason = undefined, onrestore = undefined, onempty = undefined, onprune = undefined }: Props = $props();

  const ENDS = ['any ending', 'failed', 'stopped', 'abandoned', 'limited', 'blocked', 'done', 'needs-decision', 'unrecorded', 'stalled'];

  let gone = $state<string[]>([]);
  let said = $state('');
  let emptying = $state(false);
  let clearing = $state(false);
  let prune = $state<Prune>({ outcome: '', olderThan: '30d', keep: '20' });

  const shown = $derived(entries.filter((entry) => !gone.includes(`${entry.kind}:${entry.name}`)));
  const sentence = $derived(pruneSentence(prune));
  const restoreLine = (entry: Entry) => (entry.kind === 'run' ? `$ loadout team runs restore ${entry.name}` : `$ loadout team restore ${entry.name}`);

  async function restore(entry: Entry) {
    if ((await onrestore?.(entry)) !== false) {
      gone = [...gone, `${entry.kind}:${entry.name}`];
      said = `✓ Put back: ${entry.name}.`;
    }
  }

  async function empty() {
    if ((await onempty?.()) !== false) {
      gone = entries.map((entry) => `${entry.kind}:${entry.name}`);
      said = '✓ The bin is empty.';
    }
  }

  async function clear(dry: boolean) {
    const answer = await onprune?.($state.snapshot(prune), dry);

    said = answer ? `✓ ${answer}` : dry ? '✓ Dry run: it lists what it would take and why it keeps the rest. Nothing moved.' : '✓ Cleared out. They are in the bin.';
  }
</script>

<div class="ls-bin">
  <p class="ls-note">Removed runs and teams wait here for {keptDays} days, then go for good. A run’s branches were never touched, so they are not in here.</p>
  <ul aria-label="In the bin">
    {#if !shown.length}
      <li><span></span><span class="ls-note">The bin is empty.</span><span></span></li>
    {/if}
    {#each shown as entry (`${entry.kind}:${entry.name}`)}
      <li>
        <span class="kind">{entry.kind}</span>
        <span class="t">
          <b>{entry.name}</b>
          {#if entry.what}<span>{entry.what}</span>{/if}
          <span class={entry.left <= 3 ? 'soon' : undefined}>removed {entry.removed} · {entry.left} {entry.left === 1 ? 'day' : 'days'} left{entry.size ? ` · ${entry.size}` : ''}</span>
        </span>
        <HaloButton label="Put it back" size="small" cmd={restoreLine(entry)} {disabledReason} onpress={() => void restore(entry)} />
      </li>
    {/each}
  </ul>
  <p class="ls-said" role="status">{said}</p>
  <div class="ls-answers">
    <HaloButton label="Empty it for good" kind="danger" size="small" cmd="$ loadout team bin empty --yes" disabledReason={disabledReason ?? (shown.length ? undefined : 'the bin is already empty')} onpress={() => (emptying = true)} />
  </div>
  <details>
    <summary>Clear out runs</summary>
    <div class="prune">
      <div class="two">
        <Picker label="Ending" options={ENDS.map((name) => ({ name }))} selected={Math.max(0, ENDS.indexOf(prune.outcome))} onpick={(_, index) => (prune.outcome = index ? ENDS[index] : '')} />
        <div class="ls-field">
          <label class="lab" for="prune-older">Older than</label>
          <input id="prune-older" name="prune-older" class="ls-text" type="text" placeholder="30d" autocomplete="off" bind:value={prune.olderThan} />
        </div>
        <div class="ls-field">
          <label class="lab" for="prune-keep">Keep the newest</label>
          <input id="prune-keep" name="prune-keep" class="ls-text" type="text" inputmode="numeric" placeholder="20" autocomplete="off" bind:value={prune.keep} />
        </div>
      </div>
      <p class="sentence">{sentence ?? 'Say which runs: an ending, an age or how many to keep.'}</p>
      <div class="ls-answers">
        <HaloButton label="Say which first" size="small" cmd={pruneCommand(prune, true)} disabledReason={sentence ? undefined : 'say which runs first'} onpress={() => void clear(true)} />
        <HaloButton label="Clear them out" size="small" cmd={pruneCommand(prune)} disabledReason={disabledReason ?? (sentence ? undefined : 'say which runs first')} onpress={() => (clearing = true)} />
      </div>
    </div>
  </details>
</div>

{#if emptying}
  <Dialog
    danger
    title="Empty the bin for good?"
    why="This is the one thing here with no way back. It deletes:"
    actions={[{ label: 'Empty it', kind: 'danger', cmd: '$ loadout team bin empty --yes', onpress: () => void empty() }]}
    onclose={() => (emptying = false)}
  >
    <ul class="binlist">
      {#each shown as entry (`${entry.kind}:${entry.name}`)}<li>{entry.kind} {entry.name}</li>{/each}
    </ul>
  </Dialog>
{/if}

{#if clearing}
  <Dialog
    title="Clear out these runs?"
    why={`${sentence} They wait in the bin for ${keptDays} days.`}
    actions={[{ label: 'Clear them out', kind: 'needs', cmd: pruneCommand(prune), onpress: () => void clear(false) }]}
    onclose={() => (clearing = false)}
  />
{/if}
