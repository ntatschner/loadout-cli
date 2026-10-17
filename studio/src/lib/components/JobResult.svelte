<script lang="ts">
  // One command run from Studio: how it stands, the line it ran, and what
  // it said once it ends. Read again whenever the job list changes, which
  // is how the output arrives.
  import Command from './Command.svelte';
  import HaloButton from './HaloButton.svelte';
  import Signal from './Signal.svelte';
  import { commands, SignedOut, type Job } from '../api';
  import { studio } from '../state.svelte';

  interface Props {
    id: string;
    /** Under a form, without the command's name as a heading. */
    compact?: boolean;
    /** Offered once it has ended. */
    onagain?: (job: Job) => void;
  }

  let { id, compact = false, onagain = undefined }: Props = $props();

  let job = $state<Job | null>(null);
  let problem = $state<string | null>(null);

  $effect(() => {
    void studio.jobs;

    commands.job(id).then(
      (read) => (job = read),
      (error) => {
        if (error instanceof SignedOut) {
          studio.signedOut = true;
        } else {
          problem = (error as Error).message;
        }
      },
    );
  });

  async function cancel() {
    try {
      await commands.cancel(id);
    } catch (error) {
      problem = (error as Error).message;
    }
  }

  /** The output as data when it is data, so it reads as a structure rather than one long line. */
  function pretty(output: string): string {
    try {
      return JSON.stringify(JSON.parse(output), null, 2);
    } catch {
      return output;
    }
  }

  const stateWord = (state: string) => (state === 'cancelled' ? 'stopped' : state);
  const when = (at: string) => new Date(at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  const seconds = (one: Job) => (one.endedAt ? Math.max(0, Math.round((Date.parse(one.endedAt) - Date.parse(one.startedAt)) / 1000)) : null);
</script>

{#if problem}<p class="offline" role="alert">{problem}</p>{/if}

{#if job}
  <section class="jobresult" aria-label={`Result of ${job.path}`}>
    {#if !compact}<h2 class="topicname">{job.path}</h2>{/if}
    <div class="jobhead">
      <Signal word={stateWord(job.state)} />
      <span class="calm small">started {when(job.startedAt)}{#if seconds(job) !== null} · took {seconds(job)}s{/if}{#if job.exitCode !== null} · exit code {job.exitCode}{/if}</span>
    </div>
    {#if !compact}<Command command={`$ ${job.line}`} />{/if}
    {#if job.state === 'running'}
      <div class="ls-answers flush">
        <HaloButton label="Cancel" kind="danger" size="small" cmd="stops it and anything it started" disabledReason={studio.watchOnly ? studio.watchOnlyReason : undefined} onpress={() => void cancel()} />
      </div>
      <p class="calm" role="status">Running. What it says appears here when it ends.</p>
    {:else}
      {#if job.output}
        <h3 class="ls-zone">What it said</h3>
        <!-- Focusable so a keyboard can scroll it: output wider than the page scrolls inside its box, not the page. -->
        <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
        <pre class="output" tabindex="0" aria-label="What it said">{pretty(job.output)}</pre>
      {:else}
        <p class="calm">It said nothing on standard output.</p>
      {/if}
      {#if job.error}
        <h3 class="ls-zone">On standard error</h3>
        <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
        <pre class="output err" tabindex="0" aria-label="On standard error">{job.error}</pre>
      {/if}
      {#if job.truncated}<p class="calm small">It said more than Studio keeps; the end was left out.</p>{/if}
      {#if onagain}
        <div class="ls-answers flush">
          <HaloButton label="Run it again" size="small" cmd="opens its form" onpress={() => onagain?.(job!)} />
        </div>
      {/if}
    {/if}
  </section>
{/if}
