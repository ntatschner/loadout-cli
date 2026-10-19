<script lang="ts">
  // Projects in Minimal: how many there are and where, then what is blocked
  // and what can be put right. A blocked task opens as a board of that task;
  // a finding opens as its project's health with the drawer that fixes it.
  import { untrack } from 'svelte';
  import CommandAction from '../../lib/components/CommandAction.svelte';
  import FocusFrame from '../../lib/components/FocusFrame.svelte';
  import HealthList from '../../lib/components/HealthList.svelte';
  import QuietPage from '../../lib/components/QuietPage.svelte';
  import TaskBoard from '../../lib/components/TaskBoard.svelte';
  import Projects from '../Projects.svelte';
  import { commands, readJson } from '../../lib/api';
  import { lineFor } from '../../lib/commandLine';
  import type { Finding } from '../../lib/health';
  import type { QuietItem } from '../../lib/modes';
  import { studio } from '../../lib/state.svelte';
  import { declareWords, type Chosen, type Task } from '../../lib/tasks';
  import { toasts } from '../../lib/toasts.svelte';

  let { rest, onlaunch }: { rest: string[]; onlaunch: (chosen?: Chosen) => void } = $props();

  interface Drifted {
    project: string;
    findings: (Finding & { name: string; fixable: boolean })[];
  }

  let projects = $state<{ id: string; available: boolean }[] | null>(null);
  let drift = $state<Drifted[] | null>(null);
  let open = $state<{ item: QuietItem; from: DOMRect | null } | null>(null);

  function load() {
    readJson<{ projects: { id: string; available: boolean }[] }>('project list').then((read) => (projects = read.projects), () => undefined);
    readJson<Drifted[]>('drift').then((read) => (drift = read), () => undefined);
  }

  $effect(() => untrack(load));

  const settled = $derived(studio.jobs.filter((job) => job.state !== 'running').length);
  let seen = untrack(() => settled);

  $effect(() => {
    if (settled !== seen) {
      seen = settled;
      untrack(load);
    }
  });

  // Blocked tasks from the lists Studio already holds; findings from drift across every project.
  const blocked = $derived(studio.tasks.flatMap((list) => list.tasks.filter((task) => task.state === 'blocked').map((task) => ({ ...task, project: list.project }))));
  const fixable = $derived((drift ?? []).flatMap((one) => one.findings.filter((finding) => finding.fixable).map((finding) => ({ ...finding, project: one.project }))));
  const here = $derived((projects ?? []).filter((one) => one.available).length);
  const items: QuietItem[] = $derived([
    ...blocked.map((task) => ({ id: `task:${task.project ?? '~'}/${task.id}`, kind: 'task', title: task.title, word: 'blocked', where: task.project ?? 'no project yet', ask: task.note ?? '' })),
    ...fixable.map((finding) => ({ id: `finding:${finding.project}/${finding.name}`, kind: 'finding', title: finding.name, word: 'to put right', where: finding.project, ask: finding.detail ?? '' })),
  ]);

  const whereOf = (project: string | null) => (project ? ['--project', project] : ['--global']);

  async function declare(task: Task, state: string, project: string | null) {
    const words = declareWords(task.id, state, whereOf(project));

    if (studio.watchOnly) {
      toasts.add({ level: 'info', text: 'Studio is only watching, so nothing was changed.', cmd: lineFor('task declare', words) });

      return;
    }

    await commands.run('task declare', words);
    await studio.loadJobs();
  }
</script>

{#snippet sentence()}
  {projects?.length ?? 0} project{projects?.length === 1 ? '' : 's'}, {here} on this machine.{#if blocked.length} <b>{blocked.length} task{blocked.length === 1 ? ' is' : 's are'} blocked</b>{/if}{#if blocked.length && fixable.length} and{/if}{#if fixable.length} <b>{fixable.length} thing{fixable.length === 1 ? '' : 's'} can be put right</b>{/if}{#if blocked.length || fixable.length}.{/if}
{/snippet}

{#snippet everything()}<Projects {rest} {onlaunch} />{/snippet}

<QuietPage title="Projects" sentence={projects ? sentence : undefined} {items} open={rest.length > 0} onopen={(item, from) => (open = { item, from })} {everything} calmText="Nothing is blocked, and nothing needs putting right." />

{#if open}
  <FocusFrame title={open.item.title} sub={open.item.where ?? undefined} word={open.item.word} from={open.from} onclose={() => (open = null)}>
    {#if open.item.kind === 'task'}
      {@const task = blocked.find((one) => `task:${one.project ?? '~'}/${one.id}` === open!.item.id)}
      {#if task}
        <TaskBoard tasks={[task]} where={whereOf(task.project)} disabledReason={studio.watchOnly ? studio.watchOnlyReason : undefined} ondeclare={(one, state) => void declare(one, state, task.project)} />
      {:else}
        <p class="calm">That task has moved on.</p>
      {/if}
    {:else}
      {@const project = open.item.where ?? ''}
      <HealthList findings={drift?.find((one) => one.project === project)?.findings ?? []} from={`$ loadout drift ${project}`} />
      <CommandAction path="drift" title="Put right what can be put right" preset={{ PROJECT: project, '--fix': true, '--yes': true }} fixed={['PROJECT']} label="Put it right" open />
    {/if}
  </FocusFrame>
{/if}
