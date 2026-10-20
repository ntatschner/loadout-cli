<script lang="ts">
  // A project's tasks as three columns, Open, Doing and Blocked, with done
  // and dropped folded away rather than left out. Each card offers only the
  // moves its state allows; the command under a move shows when it is
  // hovered or focused, so the cards stay calm. Given a project and a way to
  // start, a card also starts a session on its task, at once or through the
  // launch sheet with the task already chosen. A card is kept to its title
  // and a line about it; Details opens the task in full. A board can hold
  // more than one list's tasks, each card then naming its project.
  import HaloButton from './HaloButton.svelte';
  import Signal from './Signal.svelte';
  import TaskDetail from './TaskDetail.svelte';
  import { lineFor } from '../commandLine';
  import { declareWords, listHref, movedSaid, showWords, startable, startWords, TASK_MOVE, TASK_NEXT, taskKey, whereOf, whereSaid, type MovedAway, type Task } from '../tasks';

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
    /** The project the tasks are on. Without one there is nothing to start a session in. */
    project?: string;
    /** Starts a session on a task: at once, or with the launch sheet's options. */
    onstart?: (task: Task, withOptions: boolean) => void;
    /**
     * Whether the tasks come from more than one list. Each card then names
     * its project, and its moves and its start are on that project's list.
     */
    mixed?: boolean;
    /**
     * Whether the tasks are one page of a longer list. An empty column then
     * says so of this page only: what it is missing may be on another.
     */
    paged?: boolean;
    /**
     * Opens a task in full somewhere else, such as the page's address. Without
     * it the board opens the task's dialog itself.
     */
    onread?: (task: Task) => void;
  }

  let { tasks, where, from = undefined, disabledReason = undefined, ondeclare = undefined, away = [], project = undefined, onstart = undefined, mixed = false, paged = false, onread = undefined }: Props = $props();

  /** The list a task is on, by slug, or null for the workspace-wide one. */
  const listOf = (task: Task) => (mixed ? (task.project ?? null) : where[0] === '--project' ? where[1] : null);
  /** The words naming a task's list, for its moves. */
  const whereFor = (task: Task) => (mixed ? whereOf(task.project) : where);
  /** The project a session on a task would start in, if there is one. */
  const startIn = (task: Task) => (mixed ? (task.project ?? undefined) : project);

  let reading = $state<Task | null>(null);

  // A move shows at once; the next read of the list replaces it.
  let moved = $state<Record<string, string>>({});

  $effect(() => {
    void tasks;
    moved = {};
  });

  const shown = $derived(tasks.map((task) => (moved[taskKey(task)] ? { ...task, state: moved[taskKey(task)] } : task)));
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
    moved = { ...moved, [taskKey(task)]: state };
    ondeclare?.(task, state);
  }
</script>

{#snippet card(task: Task)}
  {@const into = startIn(task)}
  <article class={`task ${task.state}`} aria-label={`${task.title}, ${task.state}`}>
    <span class="tt">{task.title}</span>
    <span class="tx">{[mixed ? whereSaid(task.project) : '', task.id, task.kind, task.declaredBy, when(task.declared)].filter(Boolean).join(' · ')}</span>
    {#if task.moved}<span class="came">Moved here {movedSaid(task.moved)}</span>{/if}
    {#if task.note}<p class="note">{task.note}</p>{/if}
    <div class="moves">
      {#if into && onstart && startable(task.state)}
        <HaloButton label="Start" size="small" solid={!disabledReason} cmd={lineFor('launch', startWords(into, task.id))} {disabledReason} onpress={() => onstart(task, false)} />
        <HaloButton label="Start with options…" size="small" cmd="opens the launch sheet with this task chosen" {disabledReason} onpress={() => onstart(task, true)} />
      {/if}
      {#each TASK_NEXT[task.state] ?? [] as state (state)}
        <HaloButton label={TASK_MOVE[state]} size="small" kind={state === 'dropped' ? 'danger' : ''} cmd={lineFor('task declare', declareWords(task.id, state, whereFor(task)))} {disabledReason} onpress={() => move(task, state)} />
      {/each}
      <HaloButton label={task.hasDetail ? 'Details and history' : 'History'} size="small" cmd={lineFor('task show', showWords(task.id, listOf(task)))} onpress={() => (onread ? onread({ ...task, project: listOf(task) }) : (reading = { ...task, project: listOf(task) }))} />
    </div>
  </article>
{/snippet}

<div class="ls-tboard">
  <div class="cols">
    {#each COLUMNS as [state, title, empty] (state)}
      {@const ones = shown.filter((task) => task.state === state)}
      <section class="col" aria-labelledby={`${name}-${state}`}>
        <h4 id={`${name}-${state}`}><Signal word={state} />{title}<span class="n">{ones.length}</span></h4>
        {#each ones as task (taskKey(task))}{@render card(task)}{:else}<p class="empty">{paged ? 'None on this page.' : empty}</p>{/each}
      </section>
    {/each}
  </div>
  {#if closed.length}
    <details class="closed">
      <summary>{done} done, {closed.length - done} dropped</summary>
      <div class="grid">{#each closed as task (taskKey(task))}{@render card(task)}{/each}</div>
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

{#if reading}
  <TaskDetail task={reading} onclose={() => (reading = null)} />
{/if}
