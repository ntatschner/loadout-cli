<script lang="ts">
  // A project's tasks as three columns, Open, Doing and Blocked, with done
  // and dropped folded away rather than left out. Each card offers only the
  // moves its state allows; the command under a move shows when it is
  // hovered or focused, so the cards stay calm.
  import HaloButton from './HaloButton.svelte';
  import Signal from './Signal.svelte';
  import { lineFor } from '../commandLine';
  import { declareWords, listHref, movedSaid, TASK_MOVE, TASK_NEXT, whereSaid, type MovedAway, type Task } from '../tasks';

  interface Props {
    tasks: Task[];
    where: string[];
    /** The command the board is the same as. */
    from?: string;
    disabledReason?: string;
    ondeclare?: (task: Task, state: string) => void;
    /**
     * What left this list. Never one of its tasks: not in a column, not
     * counted, and with nothing to press but the way to where it is now.
     */
    away?: MovedAway[];
  }

  let { tasks, where, from = undefined, disabledReason = undefined, ondeclare = undefined, away = [] }: Props = $props();

  // A move shows at once; the next read of the list replaces it.
  let moved = $state<Record<string, string>>({});

  $effect(() => {
    void tasks;
    moved = {};
  });

  const shown = $derived(tasks.map((task) => (moved[task.id] ? { ...task, state: moved[task.id] } : task)));
  const COLUMNS = [
    ['open', 'Open', 'Nothing waiting to start.'],
    ['doing', 'Doing', 'Nothing under way.'],
    ['blocked', 'Blocked', 'Nothing blocked.'],
  ] as const;
  const closed = $derived(shown.filter((task) => task.state === 'done' || task.state === 'dropped'));
  const done = $derived(closed.filter((task) => task.state === 'done').length);
  const name = `tb-${Math.random().toString(36).slice(2, 7)}`;
  const when = (at: string | undefined) => (at ? new Date(at).toLocaleDateString([], { day: 'numeric', month: 'short' }) : '');

  function move(task: Task, state: string) {
    moved = { ...moved, [task.id]: state };
    ondeclare?.(task, state);
  }
</script>

{#snippet card(task: Task)}
  <article class={`task ${task.state}`} aria-label={`${task.title}, ${task.state}`}>
    <span class="tt">{task.title}</span>
    <span class="tx">{[task.id, task.kind, task.declaredBy, when(task.declared)].filter(Boolean).join(' · ')}</span>
    {#if task.moved}<span class="came">Moved here {movedSaid(task.moved)}</span>{/if}
    {#if task.note}<p class="note">{task.note}</p>{/if}
    <div class="moves">
      {#each TASK_NEXT[task.state] ?? [] as state (state)}
        <HaloButton label={TASK_MOVE[state]} size="small" kind={state === 'dropped' ? 'danger' : ''} cmd={lineFor('task declare', declareWords(task.id, state, where))} {disabledReason} onpress={() => move(task, state)} />
      {/each}
    </div>
  </article>
{/snippet}

<div class="ls-tboard">
  <div class="cols">
    {#each COLUMNS as [state, title, empty] (state)}
      {@const ones = shown.filter((task) => task.state === state)}
      <section class="col" aria-labelledby={`${name}-${state}`}>
        <h4 id={`${name}-${state}`}><Signal word={state} />{title}<span class="n">{ones.length}</span></h4>
        {#each ones as task (task.id)}{@render card(task)}{:else}<p class="empty">{empty}</p>{/each}
      </section>
    {/each}
  </div>
  {#if closed.length}
    <details class="closed">
      <summary>{done} done, {closed.length - done} dropped</summary>
      <div class="grid">{#each closed as task (task.id)}{@render card(task)}{/each}</div>
    </details>
  {/if}
  {#if away.length}
    <section class="away" aria-labelledby={`${name}-away`}>
      <h4 id={`${name}-away`}>Moved away</h4>
      <ul>
        {#each away as gone (gone.id)}
          <li>{gone.title || gone.id} moved to {whereSaid(gone.to)}, {when(gone.at)}. <a href={listHref(gone.to)}>Show it</a></li>
        {/each}
      </ul>
    </section>
  {/if}
  {#if from}<p class="ls-from">The same as <code>{from}</code>.</p>{/if}
</div>
