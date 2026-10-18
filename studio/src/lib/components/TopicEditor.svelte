<script lang="ts">
  // One memory topic, read and edited in place. A save carries the version
  // it was read at; if somebody changed the topic meanwhile, nothing is
  // saved, and what is on disk now is shown beside your edit, which stays
  // editable. Keep my edit saves yours against their version; Take theirs
  // drops it. A topic that cannot be edited here says why and gives the
  // line for a terminal.
  import { untrack } from 'svelte';
  import Command from './Command.svelte';
  import HaloButton from './HaloButton.svelte';
  import { diskLines, linesOf, replaceLine } from '../memoryEdit';
  import type { Topic } from '../api';

  interface Props {
    project: string;
    topic: Topic;
    storeLabel?: string;
    editable: boolean;
    readOnlyReason?: string;
    onsave?: (text: string, version: string) => Promise<{ saved?: Topic; changed?: Topic }>;
    onsaved?: (topic: Topic) => void;
  }

  let { project, topic, storeLabel = undefined, editable, readOnlyReason = undefined, onsave = undefined, onsaved = undefined }: Props = $props();

  // What the edit started from: the topic as read, or theirs once kept over.
  // Taken once: the page makes the editor again for another topic.
  let base = $state<Topic>(untrack(() => topic));
  let draft = $state(untrack(() => topic.text));
  let theirs = $state<Topic | null>(null);
  let saving = $state(false);
  let said = $state('');

  const dirty = $derived(draft !== base.text);
  const line = $derived(replaceLine(base.name, project, base.scope, base.version));
  const id = `te-${Math.random().toString(36).slice(2, 7)}`;
  const short = (version: string) => String(version).slice(0, 8);

  async function save() {
    if (!onsave || saving) {
      return;
    }

    saving = true;

    try {
      const result = await onsave(draft, base.version);

      if (result.changed) {
        theirs = result.changed;
        said = '';
      } else if (result.saved) {
        base = result.saved;
        draft = result.saved.text;
        theirs = null;
        said = `✓ Saved ${base.name}. It is in the workspace; loadout workspace save commits it.`;
        onsaved?.(result.saved);
      }
    } finally {
      saving = false;
    }
  }

  function keepMine() {
    if (theirs) {
      base = theirs;
      theirs = null;
      void save();
    }
  }

  function takeTheirs() {
    if (theirs) {
      base = theirs;
      draft = theirs.text;
      theirs = null;
      said = '✓ Took theirs. Your edit is gone.';
    }
  }

  const disk = $derived(theirs ? diskLines(theirs.text, draft) : []);
  const onlyDisk = $derived(disk.filter((one) => one.only).length);
  const onlyMine = $derived(theirs ? linesOf(draft).filter((one) => !linesOf(theirs!.text).includes(one)).length : 0);
</script>

<section class="ls-topic" aria-label={`Topic ${base.name}`}>
  <div class="th">
    <h2>{base.name}</h2>
    {#if base.description}<p>{base.description}</p>{/if}
    <div class="tags">
      <span class="ls-tag">{storeLabel ?? base.scope}</span>
      {#if base.kind}<span class="ls-tag">{base.kind}</span>{/if}
      {#if base.written}<span class="ls-tag">written {new Date(base.written).toLocaleDateString([], { day: 'numeric', month: 'short', year: 'numeric' })}</span>{/if}
      {#if base.version}<span class="ls-tag">version {short(base.version)}</span>{/if}
    </div>
  </div>

  {#if theirs}
    <div class="clash" role="alert">
      <b>Somebody else changed this topic while you were editing</b>
      <p>Nothing was saved. On the left is what is on disk now; on the right is your edit. Merge what you want into yours, then keep it, or take theirs and lose your edit.</p>
    </div>
    <p class="diffsum">{onlyDisk} line{onlyDisk === 1 ? '' : 's'} only on disk, marked −; {onlyMine} only in your edit.</p>
    <div class="two">
      <div>
        <p class="lab">On disk now <span>version {short(theirs.version)}{theirs.written ? ` · ${new Date(theirs.written).toLocaleString()}` : ''}</span></p>
        <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
        <pre class="disk" tabindex="0" aria-label="On disk now, read-only">{#each disk as one, index (index)}<span class={one.only ? 'only' : ''}><i aria-hidden="true">{one.only ? '−' : ''}</i>{#if one.only}<span class="ls-vh">only on disk: </span>{/if}{one.text || ' '}</span>{/each}</pre>
      </div>
      <div>
        <label class="lab" for={`${id}-mine`}>Your edit <span>from version {short(base.version)}</span></label>
        <textarea class="ls-text" id={`${id}-mine`} rows="14" bind:value={draft}></textarea>
      </div>
    </div>
    <div class="ls-answers flush">
      <HaloButton label={saving ? 'Saving…' : 'Keep my edit, then save'} kind="needs" solid size="small" cmd={replaceLine(base.name, project, base.scope, theirs.version)} onpress={keepMine} />
      <HaloButton label="Take theirs" size="small" cmd="drops your edit · changes nothing" onpress={takeTheirs} />
    </div>
  {:else}
    <label class="ls-vh" for={`${id}-text`}>The topic's text</label>
    <textarea class="ls-text" id={`${id}-text`} rows="14" spellcheck="true" readonly={!editable} bind:value={draft}></textarea>
    <div class="meta">
      <span>{linesOf(draft).length} lines · {draft.length} characters</span>
      <span class={dirty ? 'dirty' : ''}>{dirty ? 'edited, not saved' : 'as stored'}</span>
    </div>
    {#if editable}
      <div class="ls-answers flush">
        <HaloButton label={saving ? 'Saving…' : 'Save'} solid={dirty} size="small" cmd={line} disabledReason={dirty ? undefined : 'nothing changed yet'} onpress={() => void save()} />
        {#if dirty}<HaloButton label="Discard changes" size="small" cmd="changes nothing" onpress={() => (draft = base.text)} />{/if}
      </div>
    {:else}
      <div class="ro">
        <p>{readOnlyReason ?? 'This topic is read-only here.'}</p>
        <Command why="Edit it in a terminal:" command={line} />
      </div>
    {/if}
  {/if}
  <p class="ls-said" role="status">{said}</p>
</section>
