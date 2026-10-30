<script lang="ts">
  // About Loadout: the installed version with Update now, what it can do,
  // where that is explained, and the releases around it. Everything shown
  // comes from the one model 'loadout about --json' prints; the page keeps no
  // list of its own. Update now runs the job Settings runs, and shows what the
  // command says as it says it.
  import { onMount } from 'svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import UpdateSheet from '../lib/components/UpdateSheet.svelte';
  import { aboutLoadout, Refused, SignedOut, type AboutLoadout, type Job } from '../lib/api';
  import { lineFor } from '../lib/commandLine';
  import { runJob } from '../lib/jobs';
  import { studio } from '../lib/state.svelte';
  import { toasts } from '../lib/toasts.svelte';

  let model = $state<AboutLoadout | null>(null);
  let problem = $state<string | null>(null);
  let updating = $state<'install' | null>(null);
  let running = $state(false);
  let progress = $state<string[]>([]);
  let restarting = $state(false);

  const upd = $derived(studio.update);
  const refused = $derived(studio.watchOnly ? studio.watchOnlyReason : running ? 'Already updating' : undefined);
  /** Only an https address is ever a link: anything else in the model is shown as text. */
  const safe = (url: string) => /^https:\/\/\S+$/.test(url.trim());
  const lines = (text: string | null) => (text ?? '').split(/\r?\n/).filter((line) => line.trim());
  const day = (at: string | null) => (at ? new Date(at).toLocaleDateString([], { day: 'numeric', month: 'short', year: 'numeric' }) : '');

  onMount(() => {
    void load();
    void studio.loadUpdate();
  });

  async function load(refresh = false) {
    try {
      model = await aboutLoadout(refresh);
      problem = null;
    } catch (error) {
      if (error instanceof SignedOut) {
        studio.signedOut = true;
      } else {
        problem = (error as Error).message;
      }
    }
  }

  /** Runs 'loadout update' as Settings does, and shows each line it writes while it runs. */
  async function upgrade(words: string[]) {
    if (studio.watchOnly) {
      toasts.add({ level: 'info', text: 'Studio is only watching, so nothing was changed.', cmd: lineFor('update', words) });

      return;
    }

    running = true;
    progress = [];
    // The last read of the job. The update restarts the daemon, so once the
    // job has been seen running, losing the daemon is the restart it asked
    // for; a job that ended failed was read as failed before that.
    let last: Job | null = null;

    try {
      const job = await runJob('update', words, 600_000, (seen) => {
        last = seen;
        progress = lines(seen.output);
      });
      progress = lines(job.output);
      await studio.loadUpdate();
    } catch (error) {
      if (error instanceof SignedOut) {
        studio.signedOut = true;
      } else if ((last as Job | null)?.state === 'running' && (error instanceof TypeError || error instanceof Refused)) {
        // The read itself failed: no answer at all, or a daemon that has
        // already come back and never heard of the job.
        await comeBack(words);
      } else {
        toasts.add({ level: 'problem', text: (error as Error).message, cmd: lineFor('update', words) });
      }
    } finally {
      running = false;
      void studio.loadJobs();
    }
  }

  /** Waits for the restarted daemon to answer, then reads the page again, so the version shown is the one now installed. */
  async function comeBack(words: string[]) {
    restarting = true;
    const until = Date.now() + 300_000;

    try {
      while (Date.now() < until) {
        await new Promise((resolve) => setTimeout(resolve, 1000));

        try {
          model = await aboutLoadout(true);
          problem = null;
          await studio.loadUpdate();

          return;
        } catch (error) {
          if (error instanceof SignedOut) {
            studio.signedOut = true;

            return;
          }
        }
      }

      toasts.add({ level: 'problem', text: 'Loadout has not come back after the update restarted it. Start the daemon again, then look again.', cmd: lineFor('update', words) });
    } finally {
      restarting = false;
    }
  }
</script>

<div class="head"><div><h1>About Loadout</h1><span class="fact">this version, what it can do, and every release</span></div></div>

{#if problem}
  <p class="calm" role="alert">The About page could not be read: {problem}</p>
{:else if !model}
  <p class="calm" role="status">Reading…</p>
{:else}
  <section class="pblock" aria-labelledby="ab-version">
    <h2 class="ls-zone" id="ab-version">Version</h2>
    <p class="vline">
      <b>{model.version}</b>{#if model.devBuild}<span>, a development build</span>{/if}
      {#if model.updateAvailable}<span>, and <b>{model.available}</b> is available.</span>{:else if model.available}<span>, the newest release.</span>{/if}
    </p>
    <div class="ls-answers flush">
      {#if model.updateAvailable && upd && !upd.pending}
        <HaloButton label={running ? 'Updating…' : 'Update now'} kind="needs" size="small" cmd={lineFor('update', ['--restart-daemon', '--yes'])} disabledReason={refused} onpress={() => (updating = 'install')} />
      {/if}
      <HaloButton label="Look again" size="small" cmd={lineFor('about', ['--refresh'])} onpress={() => void load(true)} />
    </div>
    {#if restarting}
      <p class="calm" role="status">Restarting Loadout…</p>
    {/if}
    {#if progress.length}
      <ol class="progress" role="log" aria-label="What the update said">
        {#each progress as line, at (at)}<li>{line}</li>{/each}
      </ol>
    {/if}
  </section>

  <section class="pblock" aria-labelledby="ab-features">
    <h2 class="ls-zone" id="ab-features">What it can do</h2>
    <table class="features">
      <thead><tr><th scope="col">Feature</th><th scope="col">Since</th><th scope="col">Status</th><th scope="col">Here</th></tr></thead>
      <tbody>
        {#each model.features as feature (feature.id)}
          <tr><td>{feature.name}</td><td>{feature.since}</td><td>{feature.status}</td><td>{feature.installed ? 'yes' : 'not installed'}</td></tr>
        {/each}
      </tbody>
    </table>
  </section>

  <section class="pblock" aria-labelledby="ab-guides">
    <h2 class="ls-zone" id="ab-guides">Guides</h2>
    <ul class="guides">
      {#each model.guides as guide (guide.key)}
        <li>{#if safe(guide.url)}<a href={guide.url} target="_blank" rel="noopener noreferrer">{guide.title}</a>{:else}{guide.title}{/if}</li>
      {/each}
    </ul>
  </section>

  <section class="pblock" aria-labelledby="ab-timeline">
    <h2 class="ls-zone" id="ab-timeline">Releases</h2>
    {#if model.timeline.empty}
      <p class="calm">{model.timeline.emptyText ?? 'There is no release list to show.'}</p>
    {:else}
      {#if model.timeline.stale}
        <p class="ls-from">GitHub did not answer, so this is the list kept from {day(model.timeline.fetchedAt)}.</p>
      {/if}
      <ol class="timeline">
        {#each model.timeline.releases as release (release.version)}
          <li>
            <span class="rv"><b>{release.version}</b> {release.kind}{release.published ? `, ${day(release.published)}` : ''}</span>
            {#if !release.installed}<span class="newer">not installed</span>{/if}
            {#if safe(release.url)}<a href={release.url} target="_blank" rel="noopener noreferrer">on GitHub</a>{/if}
            {#if release.notes.trim()}
              <details><summary>Notes for {release.version}</summary><p class="notes">{release.notes}</p></details>
            {/if}
          </li>
        {/each}
      </ol>
    {/if}
  </section>
{/if}

{#if updating && upd}
  <UpdateSheet {updating} {upd} onchoose={(words) => void upgrade(words)} onclose={() => (updating = null)} />
{/if}

<style>
  .features { border-collapse: collapse; width: 100%; max-width: 72ch; }
  .features th, .features td { text-align: left; padding: 4px 12px 4px 0; }
  .timeline, .guides, .progress { padding-left: 1.2em; }
  .timeline li { margin: 6px 0; }
  .newer { margin-left: 8px; font-weight: 600; }
  .notes { white-space: pre-wrap; }
  .progress { font-family: var(--mono, monospace); }
</style>
