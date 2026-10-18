<script lang="ts">
  // Forget a project on this machine only, or from the Loadout workspace for
  // every machine, with what should go with it. Every cleanup is a real
  // command, off until ticked, run first while the project is still known;
  // the repository and its code are never touched. Forgetting it everywhere
  // asks for a second press within five seconds, because Studio runs
  // commands without prompts, where the flag counts as the confirmation.
  import { onDestroy } from 'svelte';
  import HaloButton from './HaloButton.svelte';
  import { lineFor } from '../commandLine';
  import { forgetSteps, type ForgetCounts, type ForgetPicks, type ForgetScope, type Step } from '../forget';

  interface Props {
    project: { id: string; name?: string; available?: boolean };
    counts: ForgetCounts;
    /** The folder project remove leaves in the workspace, and its files once known. */
    definition?: { path: string; files?: number | null } | null;
    disabledReason?: string;
    /** Runs the steps in order and says how it went. */
    onrun?: (plan: { scope: ForgetScope; steps: Step[]; preview: boolean }) => Promise<string>;
  }

  let { project, counts, definition = null, disabledReason = undefined, onrun = undefined }: Props = $props();

  let scope = $state<ForgetScope>('here');
  let picked = $state<ForgetPicks>({ protect: false, tasks: false, ideas: false, topics: false });
  let armed = $state(false);
  let running = $state(false);
  let said = $state('');
  let timer: ReturnType<typeof setTimeout> | null = null;
  const name = `fg-${Math.random().toString(36).slice(2, 7)}`;

  const slug = $derived(project.id);
  const away = $derived(project.available === false);
  const steps = $derived(forgetSteps(slug, scope, picked, counts, !away));
  const local = 'Only when forgetting it from the workspace: on this machine only, the workspace keeps them for your other machines.';

  function disarm() {
    armed = false;

    if (timer) {
      clearTimeout(timer);
      timer = null;
    }
  }

  onDestroy(disarm);

  function choose(next: ForgetScope) {
    scope = next;
    disarm();
  }

  function tick(key: keyof ForgetPicks, on: boolean) {
    picked = { ...picked, [key]: on };
    disarm();
  }

  async function go(preview: boolean) {
    if (!onrun || running) {
      return;
    }

    running = true;
    said = '';

    try {
      said = await onrun({ scope, steps: forgetSteps(slug, scope, picked, counts, !away, preview), preview });
    } finally {
      running = false;
    }
  }

  function forget() {
    if (scope === 'everywhere' && !armed) {
      armed = true;
      timer = setTimeout(disarm, 5000);

      return;
    }

    disarm();
    void go(false);
  }

  const n = (list: string[] | null, one: string, many: string) => (list === null ? many : `${list.length} ${list.length === 1 ? one : many}`);
  // Why a workspace cleanup cannot be ticked: on this machine only, still being read, or nothing to remove.
  const offFor = (list: string[] | null) => (scope === 'here' ? local : list === null ? 'Still reading what it has.' : list.length ? null : 'It has none.');
  const many = (list: string[] | null, line: string) => (list?.length ? `${line}  × ${list.length}` : null);
  const cleanups = $derived([
    { key: 'protect' as const, title: 'Take the Git protections out of its repository', sub: 'The hooks and ignore rules Loadout installed to keep AI files out of commits.', cmd: lineFor('protect', [slug, '--remove']), off: away ? 'It is not on this machine, so there is no repository here to change.' : null },
    { key: 'tasks' as const, title: `Its ${n(counts.tasks, 'task', 'tasks')}`, sub: 'Every task recorded for it, open or not.', cmd: many(counts.tasks, lineFor('task remove', ['<id>', '--project', slug])), off: offFor(counts.tasks) },
    { key: 'ideas' as const, title: `Its ${n(counts.ideas, 'idea', 'ideas')}`, sub: 'Ideas being shaped for it. Accepted plans stay where they were written.', cmd: many(counts.ideas, lineFor('idea remove', ['<id>', '--project', slug])), off: offFor(counts.ideas) },
    { key: 'topics' as const, title: `Its ${n(counts.topics, 'memory topic', 'memory topics')}`, sub: 'What agents remembered about it. A backup of each is taken first.', cmd: many(counts.topics, lineFor('memory delete', ['<topic>', slug, '--yes'])), off: offFor(counts.topics) },
  ]);
</script>

<div class="ls-forget">
  <p class="lead">Loadout forgets {project.name || slug}. Its repository and code are never touched.</p>

  <fieldset class="where">
    <legend>Forget it</legend>
    <label><input type="radio" name={name} value="here" checked={scope === 'here'} onchange={() => choose('here')} /><b>On this machine only</b><span>Loadout forgets where it is here. It stays in the workspace, listed as not on this machine, and your other machines keep it.</span></label>
    <label class="wide"><input type="radio" name={name} value="everywhere" checked={scope === 'everywhere'} onchange={() => choose('everywhere')} /><b>From the Loadout workspace</b><span>Every machine forgets it, and it leaves the list. Its tasks, ideas and memory can go with it.</span></label>
  </fieldset>

  <fieldset class="clean">
    <legend>Also clean up</legend>
    {#each cleanups as one (one.key)}
      <label class={one.off ? 'off' : ''}>
        <input type="checkbox" name={`forget-${one.key}`} disabled={!!one.off} checked={!one.off && picked[one.key]} onchange={(event) => tick(one.key, (event.currentTarget as HTMLInputElement).checked)} />
        <b>{one.title}</b>
        <span>{one.off ?? one.sub}</span>
        {#if one.cmd}<code>{one.cmd}</code>{/if}
      </label>
    {/each}
  </fieldset>

  {#if scope === 'everywhere' && definition}
    <p class="kept">Kept in the workspace whatever you tick: its instructions, rules and memory folder{definition.files != null ? `, ${definition.files} file${definition.files === 1 ? '' : 's'}` : ''} under <code>{definition.path}</code>. Loadout never deletes it; delete it yourself if you meant to lose it.</p>
  {/if}

  <div>
    <p class="lab">What will run, in order</p>
    <ol class="plan" aria-label="What will run, in order">{#each steps as one, index (index)}<li>{one.line}</li>{/each}</ol>
  </div>

  <p class="armed" role="status">{armed ? `Every machine that uses the workspace forgets ${slug}. Press the button again within five seconds to go ahead.` : ''}</p>

  <div class="ls-answers flush">
    <HaloButton label="Preview" size="small" cmd="adds --dry-run to each · changes nothing" disabledReason={running ? 'running…' : undefined} onpress={() => void go(true)} />
    <HaloButton
      label={armed ? 'Press again to forget it everywhere' : scope === 'everywhere' ? 'Forget it on every machine' : 'Forget it here'}
      kind="danger"
      size="small"
      solid={armed && !disabledReason}
      cmd={`${steps.length} command${steps.length === 1 ? '' : 's'}, in order; stops at the first that fails`}
      disabledReason={disabledReason ?? (running ? 'running…' : undefined)}
      onpress={forget}
    />
  </div>
  <p class="ls-said" role="status">{said}</p>
</div>
