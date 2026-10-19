<script lang="ts">
  // New session: everything a launch asks, filled in with the defaults. What
  // the session would load is worked out by the daemon with the same resolver
  // the terminal launch sheet and --dry-run use, and the command beneath is
  // the one the daemon will run. Opened from a task, it starts with that task
  // and its project chosen; nothing else about it changes.
  import { onMount } from 'svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import Meter from '../lib/components/Meter.svelte';
  import ModePicker from '../lib/components/ModePicker.svelte';
  import Picker from '../lib/components/Picker.svelte';
  import Switch from '../lib/components/Switch.svelte';
  import { launchSheet, type Launch, type Preview, type Sheet } from '../lib/api';
  import { roving } from '../lib/roving';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';
  import type { Chosen } from '../lib/tasks';

  let { task: typed = '', chosen = null, onclose }: { task?: string; chosen?: Chosen | null; onclose: () => void } = $props();

  const MODE_WORDS: Record<string, string> = {
    '': 'chosen with the specialists for the task',
    advise: "Answer the question. Don't change the repository.",
    investigate: "Find out what's happening. The fix is a separate step.",
    implement: 'Make the change and verify it.',
    review: "Judge the change as written. Report, don't rewrite.",
  };
  const STATE_ORDER: Record<string, number> = { doing: 0, open: 1, blocked: 2 };
  const OTHER = '__other';

  let sheet = $state<Sheet | null>(null);
  let problem = $state<string | null>(null);
  let project = $state<string | null>(null);
  let agent = $state<string | null>(null);
  let taskId = $state<string | null>(null);
  let other = $state('');
  let mode = $state('');
  let profile = $state<string | null>(null);
  let worktree = $state<string | null>(null);
  let offline = $state(false);
  let noSync = $state(false);
  let handoff = $state(false);
  let preview = $state<Preview | null>(null);

  const taskButtons = $state<(HTMLButtonElement | undefined)[]>([]);

  async function read(slug: string | null, named: string | null) {
    try {
      sheet = await launchSheet.read(slug, named);
      project = sheet.project;
      agent = sheet.agent;
      worktree = null;
      profile = null;
      // What the project is doing now comes first; a task typed on Home
      // arrives as Other, so it is not lost.
      taskId = chosen && chosen.project === sheet.project ? chosen.id : typed ? OTHER : (sheet.tasks.find((one) => one.state === 'doing')?.id ?? null);
      other = typed;
    } catch (error) {
      problem = (error as Error).message;
    }
  }

  onMount(() => void read(chosen?.project ?? null, null));

  /**
   * No task, then doing, open and blocked, then Other… A done task the
   * sheet was opened from is not one the sheet lists, so it comes first.
   */
  function tasks(): { id: string | null; title: string; state: string }[] {
    const declared = [...(sheet?.tasks ?? [])]
      .filter((one) => one.state in STATE_ORDER)
      .sort((a, b) => STATE_ORDER[a.state] - STATE_ORDER[b.state]);
    const from = chosen && chosen.project === sheet?.project && !declared.some((one) => one.id === chosen.id) ? [{ id: chosen.id, title: chosen.title, state: chosen.state }] : [];

    return [{ id: null, title: 'No task', state: '' }, ...from, ...declared, { id: OTHER, title: 'Other…', state: '' }];
  }

  const launch: Launch | null = $derived(
    project
      ? {
          project,
          agent: agent && agent !== sheet?.defaultAgent ? agent : undefined,
          taskId: taskId && taskId !== OTHER ? taskId : undefined,
          task: taskId === OTHER && other.trim() ? other.trim() : undefined,
          mode: mode || undefined,
          profile: profile ?? undefined,
          worktree: worktree ?? undefined,
          offline: offline || undefined,
          noSync: noSync || undefined,
          handoff: handoff || undefined,
        }
      : null,
  );

  let asked = 0;

  $effect(() => {
    // Ask again whenever anything changes, keeping only the latest answer.
    const asking = launch;

    if (!asking) {
      return;
    }

    const mine = ++asked;
    const timer = setTimeout(() => {
      launchSheet.preview(asking).then(
        (answer) => mine === asked && (preview = answer),
        (error) => mine === asked && (preview = { command: null, error: (error as Error).message }),
      );
    }, 150);

    return () => clearTimeout(timer);
  });

  const shown = $derived(preview?.command ? `$ ${preview.command}` : '');
  const budget = $derived(preview?.budget ?? (sheet ? { estimatedTokens: 0, tokenBudget: sheet.budget.tokenBudget, warnAtPercent: sheet.budget.warnAtPercent } : null));

  const unable = $derived(
    studio.watchOnly
      ? studio.watchOnlyReason
      : studio.about?.startsSessions === false
        ? 'This daemon cannot start a session here'
        : !project
          ? 'Pick a project first'
          : preview?.error
            ? preview.error
            : undefined,
  );

  async function start(quick: boolean) {
    if (!project) {
      return true;
    }

    const asking: Launch = quick ? { project } : launch!;
    const words = quick ? `$ loadout launch ${project}` : shown;
    const started = await studio.startSession(asking, words);

    if (started) {
      onclose();
      router.go('session', started.id);
    }

    return true;
  }

  function pickTask(index: number) {
    taskId = tasks()[index].id;

    if (taskId === OTHER) {
      queueMicrotask(() => document.getElementById('other-task')?.focus());
    }
  }
</script>

<Dialog
  title="New session"
  why="Everything a launch asks is here, filled in with the defaults. The command below is the launch; Studio runs it for you, and the session keeps running if you close this tab."
  extraClass="wide"
  actions={[
    { label: 'Launch', solid: true, glyph: '▶', cmd: unable ?? 'runs the command above', disabledReason: unable, onpress: () => start(false) },
    { label: 'Quick session', cmd: project ? `$ loadout launch ${project} · no task, the defaults` : 'needs a project', disabledReason: studio.watchOnly ? studio.watchOnlyReason : undefined, onpress: () => start(true) },
  ]}
  {onclose}
>
  {#if problem}
    <p class="offline" role="alert">{problem}</p>
  {:else if !sheet}
    <p class="calm" role="status">Reading the projects…</p>
  {:else if !sheet.projects.length}
    <p class="calm">Loadout knows no project yet. Add one in a terminal: <code class="ls-cmd">$ loadout project add</code></p>
  {:else}
    <div class="sheet">
      <div class="grid2">
        {#key sheet.project}
          <Picker
            label="Project"
            selected={Math.max(0, sheet.projects.findIndex((one) => one.slug === project))}
            options={sheet.projects.map((one) => ({ name: one.slug, sub: one.here ? (one.path ?? '') : 'not on this machine' }))}
            onpick={(option) => void read(option.name, null)}
          />
        {/key}
        <Picker
          label="Mode"
          options={['', ...sheet.modes].map((one) => ({ name: one || '(let the task decide)', sub: MODE_WORDS[one] ?? '' }))}
          onpick={(_, index) => (mode = index === 0 ? '' : sheet!.modes[index - 1])}
        />
      </div>

      <div class="ls-field taskfield">
        <span class="lab" id="task-lab">What are you about to do?</span>
        <div class="tasks" role="radiogroup" aria-labelledby="task-lab">
          {#each tasks() as one, index (one.id ?? '(none)')}
            <button
              bind:this={taskButtons[index]}
              type="button"
              role="radio"
              aria-checked={taskId === one.id ? 'true' : 'false'}
              tabindex={taskId === one.id ? 0 : -1}
              onclick={() => pickTask(index)}
              onkeydown={(event) => roving(event, index, taskButtons, pickTask)}
            ><span class="st">{one.state}</span><span>{one.title}</span></button>
          {/each}
        </div>
        {#if taskId === OTHER}
          <div class="other">
            <input id="other-task" name="other-task" placeholder="Describe a one-off task" aria-label="Other task" bind:value={other} autocomplete="off" />
          </div>
        {/if}
      </div>

      <div class="grid2">
        <div>
          <span class="lab sheetlab">Agent</span>
          {#key sheet.project}
            <ModePicker
              label="Agent"
              selected={Math.max(0, sheet.agents.findIndex((one) => one.name === agent))}
              options={sheet.agents.map((one) => ({ name: one.name, desc: `${one.display}${one.name === sheet!.defaultAgent ? ' · this project’s agent' : ''}${one.installed ? '' : ' · not installed'}` }))}
              onchange={(name) => (agent = name)}
            />
          {/key}
        </div>
        <div class="stack">
          {#if sheet.worktrees.length > 1}
            <Picker label="Worktree" options={sheet.worktrees.map((one) => ({ name: one.label, sub: one.value ?? 'the project’s own checkout' }))} onpick={(_, index) => (worktree = sheet!.worktrees[index].value)} />
          {/if}
          {#if sheet.profiles.length > 1}
            <Picker label="Profile" options={sheet.profiles.map((one) => ({ name: one.label, sub: one.value ?? 'the project’s default' }))} onpick={(_, index) => (profile = sheet!.profiles[index].value)} />
          {/if}
        </div>
      </div>

      <div class="optsw">
        <Switch label="Work offline" sub="--offline" bind:checked={offline} />
        <Switch label="Skip workspace sync" sub="--no-sync" bind:checked={noSync} />
        <Switch label="Append last handoff" sub="--handoff" bind:checked={handoff} />
      </div>

      <div class="loads">
        <h3>This session would load</h3>
        {#if preview?.error}
          <p class="offline">{preview.error}</p>
        {:else if preview?.specialists}
          <dl>
            {#each preview.specialists as one (one.id)}
              <dt>{one.kind}</dt><dd>{one.id} · {one.reason} · about {one.estimatedTokens.toLocaleString('en-GB')} tokens</dd>
            {/each}
            {#if preview.mode}<dt>mode</dt><dd>{preview.mode}{mode ? '' : ' · chosen by the task'}</dd>{/if}
          </dl>
        {:else}
          <p class="calm" role="status">Working it out…</p>
        {/if}
        {#if budget}
          <Meter
            label="Specialists against the instruction budget"
            value={budget.estimatedTokens}
            max={budget.tokenBudget}
            threshold={(budget.tokenBudget * budget.warnAtPercent) / 100}
            unit="tokens"
            thresholdLabel={`warns at ${budget.warnAtPercent}%`}
            format={(v) => `about ${v.toLocaleString('en-GB')}`}
            of={(v) => `${Math.round((v / budget.tokenBudget) * 100)}% of ${budget.tokenBudget.toLocaleString('en-GB')}`}
          />
        {/if}
      </div>
      <code class="ls-cmdline">{shown || '…'}</code>
    </div>
  {/if}
</Dialog>
