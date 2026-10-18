<script lang="ts">
  // Forget <project>, in a Dialog: ForgetProject with what the project has
  // read for it, and the runner that does each line as a job, waits for it,
  // and stops at the first that does not finish cleanly. Opened from the
  // project's dossier and from its actions in Pure Focus.
  import Dialog from './Dialog.svelte';
  import ForgetProject from './ForgetProject.svelte';
  import { commands, memory, readJson, SignedOut, type Job } from '../api';
  import { filesLeft, runSteps, type ForgetScope, type Step } from '../forget';
  import { studio } from '../state.svelte';

  let { project, onclose }: { project: { id: string; name: string; available: boolean }; onclose: () => void } = $props();

  let tasks = $state<string[] | null>(null);
  let ideas = $state<string[] | null>(null);
  let topics = $state<string[] | null>(null);
  let definition = $state<{ path: string; files?: number | null } | null>(null);

  // Read once per project. The dossier reads the project again after every
  // job and hands this a fresh copy; following that would read the counts
  // again mid-run and drop the file count project remove reported.
  const projectId = $derived(project.id);

  $effect(() => {
    const slug = projectId;

    readJson<{ tasks: { id: string }[] }>('task list', ['--project', slug, '--all']).then((read) => (tasks = read.tasks.map((one) => one.id)), () => (tasks = null));
    readJson<{ ideas: { id: string; project?: string | null }[] }>('idea list').then((read) => (ideas = read.ideas.filter((one) => one.project === slug).map((one) => one.id)), () => (ideas = null));
    memory.topics(slug).then((read) => (topics = read.topics.filter((one) => one.scope === 'project').map((one) => one.name)), () => (topics = null));
    // project remove leaves <workspace>/projects/<slug>, and says how many files once it has run.
    readJson<{ localPath: string }>('workspace status').then((read) => {
      // The workspace's own separator, without a backslash in the source.
      const sep = read.localPath.includes(String.fromCharCode(92)) ? String.fromCharCode(92) : '/';

      definition = { path: [read.localPath, 'projects', slug].join(sep) };
    }, () => (definition = null));
  });

  /** Runs one command as a job and waits for it to end. */
  async function runToEnd(one: Step): Promise<Job | null> {
    try {
      let job = await commands.run(one.path, one.words);

      while (job.state === 'running') {
        await new Promise((resolve) => setTimeout(resolve, 400));
        job = await commands.job(job.id);
      }

      await studio.loadJobs();

      return job;
    } catch (error) {
      if (error instanceof SignedOut) {
        studio.signedOut = true;
      }

      return null;
    }
  }

  async function forget(plan: { scope: ForgetScope; steps: Step[]; preview: boolean }): Promise<string> {
    let last: Job | null = null;
    const outcome = await runSteps(plan.steps, async (one) => {
      last = await runToEnd(one);

      return last?.state === 'done';
    });

    if (outcome.failed) {
      return `Stopped at ${outcome.failed.line}: it did not finish cleanly, so nothing after it ran. ${outcome.ran} of ${plan.steps.length} ran.`;
    }

    if (plan.preview) {
      return `Previewed all ${plan.steps.length}. Nothing changed.`;
    }

    const files = last ? filesLeft((last as Job).output ?? '') : null;

    if (files !== null && definition) {
      definition = { ...definition, files };
    }

    return plan.scope === 'everywhere' ? 'Forgotten on every machine. Its folder is still in the workspace, as it says above.' : 'Forgotten on this machine. It stays on the list as not on this machine.';
  }
</script>

<!-- The keyboard starts on the first choice, never on the button that forgets. -->
<Dialog title={`Forget ${project.name}`} danger cancelLabel="Close" extraClass="wide" initialFocus={'.ls-forget input[value="here"]'} {onclose}>
  <ForgetProject {project} counts={{ tasks, ideas, topics }} {definition} disabledReason={studio.watchOnly ? studio.watchOnlyReason : undefined} onrun={forget} />
</Dialog>
