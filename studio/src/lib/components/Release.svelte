<script lang="ts" module>
  /** What 'loadout release plan <project> --json' gives. */
  export interface ReleasePlanRead {
    project: string;
    recipe: 'tag-push' | 'workflow' | 'none';
    branch: string;
    lastTag: string | null;
    versionFile: string;
    fileVersion: string | null;
    proposed: string | null;
    bump: string;
    since: string[];
    notes: string;
    problems: string[];
  }

  /** The words for 'loadout release cut', joined with '=' so text starting with a dash is still a value. */
  export function cutWords(project: string, version: string, notes: string): string[] {
    return [project, `--version=${version.trim()}`, `--notes=${notes.trim()}`, '--yes'];
  }
</script>

<script lang="ts">
  import HaloButton from './HaloButton.svelte';

  // Releasing a project: the plan read first, the version and notes edited
  // from what it proposes, and a second press that says what will be pushed.
  // The same commands as the terminal, and the second press is the say-so
  // the terminal asks for, so it passes --yes.
  interface Props {
    /** Projects to offer, such as those with branches waiting or landed. */
    projects: string[];
    /** Reads a project's release plan. */
    read: (project: string) => Promise<ReleasePlanRead>;
    /** Runs a command and refreshes the page; false when it did not go through. */
    act?: (path: string, words: string[], done: string) => Promise<boolean | void> | boolean | void;
    /** When set, nothing can be pressed, and this says why. */
    disabledReason?: string;
  }

  let { projects, read, act = undefined, disabledReason = undefined }: Props = $props();

  let project = $state('');
  let plan = $state<ReleasePlanRead | null>(null);
  let problem = $state<string | null>(null);
  let reading = $state(false);
  let version = $state('');
  let notes = $state('');
  let confirming = $state(false);

  async function look() {
    if (!project.trim()) {
      return;
    }

    reading = true;
    problem = null;
    confirming = false;

    try {
      plan = await read(project.trim());
      version = plan.proposed ?? '';
      notes = plan.notes;
    } catch (error) {
      plan = null;
      problem = error instanceof Error ? error.message : String(error);
    } finally {
      reading = false;
    }
  }

  const blocked = () =>
    !plan
      ? 'Read the plan first'
      : plan.problems.length
        ? plan.problems[0]
        : !/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/.test(version.trim())
          ? 'Give the version as major.minor.patch'
          : !notes.trim()
            ? 'Write the release notes'
            : undefined;

  const tag = () => `${plan?.lastTag?.match(/^\D*/)?.[0] || 'v'}${version.trim()}`;

  const bumps = () => plan?.fileVersion !== version.trim();

  const does = () =>
    plan?.recipe === 'workflow'
      ? `Starts ${plan.project}'s release workflow on ${plan.branch} for ${version.trim()}.`
      : `${bumps() ? `Bumps ${plan?.versionFile} to ${version.trim()} and commits it on ${plan?.branch}, then tags` : 'Tags'} ${tag()} with these notes, checks the tagged commit says ${version.trim()}, and pushes to origin.`;

  async function cut() {
    if (!plan || (await act?.('release cut', cutWords(plan.project, version, notes), `${tag()} is released`)) === false) {
      return;
    }

    confirming = false;
    await look();
  }
</script>

<div class="release">
  <div class="ls-field">
    <label class="lab" for="rel-project">Project</label>
    <input id="rel-project" class="ls-text" list="rel-projects" bind:value={project} placeholder="A project's slug" />
    <datalist id="rel-projects">
      {#each projects as one (one)}<option value={one}></option>{/each}
    </datalist>
  </div>
  <HaloButton label="Read the plan" size="small" cmd={`$ loadout release plan ${project.trim() || '<project>'}`} disabledReason={!project.trim() ? 'Name a project' : reading ? 'Reading…' : undefined} onpress={() => void look()} />

  {#if problem}<p class="small rproblem" role="alert">{problem}</p>{/if}

  {#if plan}
    <p class="small">{plan.project} releases with <strong>{plan.recipe}</strong> from {plan.branch}. Last release: {plan.lastTag ?? 'none'}{plan.versionFile ? `; ${plan.versionFile} says ${plan.fileVersion ?? 'nothing readable'}` : ''}.</p>
    <p class="calm small">{plan.proposed ? `Proposed ${plan.proposed}: ${plan.bump}.` : `Nothing proposed: ${plan.bump}.`} {plan.since.length} commit(s) since.</p>
    {#each plan.problems as one (one)}<p class="small rproblem">{one}</p>{/each}

    {#if !plan.problems.length && act}
      <div class="ls-field">
        <label class="lab" for="rel-version">Version</label>
        <input id="rel-version" class="ls-text" bind:value={version} />
      </div>
      <div class="ls-field">
        <label class="lab" for="rel-notes">Notes</label>
        <textarea id="rel-notes" class="ls-text" rows="5" bind:value={notes}></textarea>
      </div>
      {#if confirming}
        <p class="small">{does()} This can't be taken back once it is pushed.</p>
        <HaloButton label={`Release ${tag()}`} kind="danger" solid cmd={`$ loadout release cut ${plan.project} --version ${version.trim()} --notes … --yes`} disabledReason={disabledReason ?? blocked()} onpress={() => void cut()} />
        <HaloButton label="Not now" size="small" onpress={() => (confirming = false)} />
      {:else}
        <HaloButton label="Release…" cmd="shows what it will push before it does" disabledReason={disabledReason ?? blocked()} onpress={() => (confirming = true)} />
      {/if}
    {/if}
  {/if}
</div>

<style>
  .release {
    display: grid;
    gap: var(--space-2);
    max-width: 68ch;
  }

  .release p {
    margin: 0;
  }

  .rproblem {
    color: var(--needs-text);
  }
</style>
