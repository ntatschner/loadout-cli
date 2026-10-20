<script lang="ts">
  // One task in full, opened from its card: the detail a list leaves out, the
  // note, and every change of state with who said so and when. Read with
  // 'task show', so it says what a terminal would. The card already has the
  // title and the moves; this is for reading.
  import { onMount } from 'svelte';
  import Dialog from './Dialog.svelte';
  import HaloButton from './HaloButton.svelte';
  import Markdown from './Markdown.svelte';
  import Signal from './Signal.svelte';
  import { readJson, SignedOut } from '../api';
  import { lineFor } from '../commandLine';
  import { studio } from '../state.svelte';
  import { ideaHref, showWords, whereSaid, type Task, type TaskShown } from '../tasks';

  interface Props {
    /** The task as the card has it, shown until the full one is read. */
    task: Task;
    onclose: () => void;
  }

  let { task, onclose }: Props = $props();

  let full = $state<TaskShown | null>(null);
  let problem = $state<string | null>(null);

  const words = $derived(showWords(task.id, task.project));
  const when = (at: string) => new Date(at).toLocaleString([], { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });

  onMount(() => {
    readJson<TaskShown>('task show', words).then(
      (read) => (full = read),
      (error) => {
        if (error instanceof SignedOut) {
          studio.signedOut = true;
        } else {
          problem = (error as Error).message;
        }
      },
    );
  });
</script>

<!-- Opened from an address, the task is known only by its id and list until it is read. -->
<Dialog title={full?.title || task.title || task.id} extraClass="ls-tdetail" initialFocus=".close button" {onclose}>
  <p class="chips">
    {#if full?.state || task.state}<span class="ls-tag"><Signal word={full?.state || task.state} /></span>{/if}
    <span class="ls-tag">{task.id}</span>
    {#if task.project !== undefined}<span class="ls-tag">{whereSaid(task.project)}</span>{/if}
  </p>

  {#if problem}
    <p class="offline" role="alert">{problem}</p>
  {:else if !full}
    <p class="calm" role="status">Reading…</p>
  {:else}
    {#if full.idea}
      <!-- Linked by the id the idea and its task share, as 'task show' works it out. -->
      <p class="from">From an idea accepted {when(full.idea.accepted)}: the plan is at <code>{full.idea.plan}</code>. <a href={ideaHref(task.project, task.id)} onclick={onclose}>Open the idea</a></p>
    {/if}
    {#if full.note}
      <section aria-labelledby="td-note">
        <h3 id="td-note">Note</h3>
        <p>{full.note}</p>
      </section>
    {/if}
    {#if full.detail}
      <section aria-labelledby="td-detail">
        <h3 id="td-detail">Detail</h3>
        <Markdown source={full.detail} />
      </section>
    {/if}
    <section aria-labelledby="td-history">
      <h3 id="td-history">History</h3>
      {#if full.history.length}
        <ol class="history">
          {#each full.history as one, n (n)}
            <li><span class="st"><Signal word={one.state} /></span> <span class="by">{one.by || 'nobody named'}</span> <time datetime={one.at}>{when(one.at)}</time>{#if one.reason}<span class="why">{one.reason}</span>{/if}</li>
          {/each}
        </ol>
      {:else}
        <!-- Recorded before there was a history: the latest claim is all that was kept. -->
        <p class="calm">{full.state}, said by {full.declaredBy || 'nobody named'}{full.declared ? ` on ${when(full.declared)}` : ''}. Nothing earlier was kept.</p>
      {/if}
    </section>
  {/if}

  <p class="ls-from">The same as <code>{lineFor('task show', words)}</code>.</p>
  <div class="close"><HaloButton label="Close" size="small" onpress={onclose} /></div>
</Dialog>
