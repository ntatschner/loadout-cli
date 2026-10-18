<script lang="ts">
  // One command's form: its arguments and options as the command declares
  // them, the line it will run shown as it is filled in, and Run (with
  // Preview for one that changes something). A page passes the values it
  // already knows, such as the project, and can fix them so they are not
  // asked for again. A command left to the terminal shows its line and why,
  // and one that starts a session sends you to Sessions.
  import { untrack } from 'svelte';
  import Command from './Command.svelte';
  import HaloButton from './HaloButton.svelte';
  import TerminalOnly from './TerminalOnly.svelte';
  import JobResult from './JobResult.svelte';
  import { commands, sessions, SignedOut, type Job, type StudioCommand, type StudioOption } from '../api';
  import { router } from '../router.svelte';
  import { lineFor, problemWith, readsWith, wordsFor } from '../commandLine';
  import { studio } from '../state.svelte';

  interface Props {
    command: StudioCommand;
    /** Values to start the form with, by argument or option name. */
    preset?: Record<string, string | boolean>;
    /** Names whose preset values are used but not shown as fields. */
    fixed?: string[];
    /** The Run button's word. */
    label?: string;
    /** Called once a job has started. Without it, the result shows under the form. */
    onstarted?: (job: Job) => void;
    /** Opens New session, for a command that starts one. */
    onlaunch?: () => void;
    /** Hide the description, for a page that has already said what this does. */
    plain?: boolean;
    /**
     * Drawn inside an ActionDrawer, which already shows the effect: the
     * button that changes something is the solid one, and a disabled one is
     * never solid.
     */
    drawer?: boolean;
    /** Said in place of the command's own description. */
    why?: string;
    /** The button acknowledges a loss. */
    danger?: boolean;
  }

  let { command, preset = {}, fixed = [], label = 'Run', onstarted = undefined, onlaunch = undefined, plain = false, drawer = false, why = undefined, danger = false }: Props = $props();

  // Text for arguments and valued options, true or false for flags.
  // Loosely typed on purpose: the form's shape is the command's, known only at run time.
  let values = $state<Record<string, any>>({});
  let problem = $state<string | null>(null);
  let starting = $state(false);
  let started = $state<string | null>(null);

  // A new command, or new values from the page, start the form again. By
  // what the preset says rather than which object it is: a page reading its
  // data again builds a new one with the same values, and resetting then
  // would clear the result of what was just run.
  const presetKey = $derived(JSON.stringify(preset));
  const commandPath = $derived(command.path);

  $effect(() => {
    void commandPath;
    void presetKey;

    untrack(() => {
      values = { ...preset };
      problem = null;
      started = null;
    });
  });

  const EFFECT: Record<string, string> = {
    reads: 'reads only',
    changes: 'changes something',
    session: 'starts a session',
    terminalOnly: 'terminal only',
  };

  const shownArguments = $derived(command.arguments.filter((one) => !fixed.includes(one.name)));
  const own = $derived(command.options.filter((one) => !one.global && !fixed.includes(one.name)));
  const everyCommand = $derived(command.options.filter((one) => one.global && one.name !== '--non-interactive' && !fixed.includes(one.name)));
  const words = $derived(wordsFor(command, values));
  const notYet = $derived(problemWith(command, values));
  const reads = $derived(readsWith(command, words));
  const previewable = $derived(!reads && command.options.some((one) => one.name === '--dry-run'));
  const runReason = $derived(notYet ?? (studio.watchOnly && !reads ? 'Studio is only watching: preview it, or type the command in a terminal' : undefined));
  // A command that acts only when given an option reads until it is, and its button says so.
  const acting = $derived(command.reach.effect === 'changes' ? (command.reach.changesOnlyWith ?? []) : []);
  const runCmd = $derived(reads ? (acting.length ? `reads only · changes with ${acting.join(' or ')}` : 'reads only') : 'changes something');

  // A command that asks as it goes is not a job, since nobody could answer
  // it: it opens in a terminal the daemon hosts, and the page goes to it.
  const asks = $derived(command.reach.effect === 'session');

  async function openTerminal() {
    starting = true;
    problem = null;

    try {
      const opened = await sessions.start({ project: '', command: command.path, words, columns: 120, rows: 32 });

      router.go('session', opened.id);
    } catch (error) {
      if (error instanceof SignedOut) {
        studio.signedOut = true;
      } else {
        problem = (error as Error).message;
      }
    } finally {
      starting = false;
    }
  }

  async function run(preview: boolean) {
    starting = true;
    problem = null;

    try {
      const job = await commands.run(command.path, preview && !words.includes('--dry-run') ? [...words, '--dry-run'] : words);

      await studio.loadJobs();

      if (onstarted) {
        onstarted(job);
      } else {
        started = job.id;
      }
    } catch (error) {
      if (error instanceof SignedOut) {
        studio.signedOut = true;
      } else {
        problem = (error as Error).message;
      }
    } finally {
      starting = false;
    }
  }

  const id = (name: string) => `f-${command.path.replace(/[^a-z0-9]/gi, '-')}-${name.replace(/[^a-z0-9]/gi, '-')}`;
</script>

{#if drawer}
  {#if why ?? command.description}<p class="why">{why ?? command.description}</p>{/if}
{:else if !plain}
  <p class="calm">{command.description} <span class="effect">· {EFFECT[command.reach.effect]}</span></p>
{/if}

{#if problem}<p class="offline" role="alert">{problem}</p>{/if}

{#if command.reach.effect === 'terminalOnly'}
  <TerminalOnly title="Studio does not run this" why={`It ${command.reach.reason}.`} command={`$ loadout ${command.path}`} />
{:else if command.reach.effect === 'session' && command.path === 'launch'}
  <p class="calm">This starts an agent session. Studio hosts sessions from Sessions, where you can type into them.</p>
  {#if onlaunch}
    <div class="ls-answers flush">
      <HaloButton label="Start a session…" solid size="small" cmd="opens New session" disabledReason={studio.watchOnly ? studio.watchOnlyReason : undefined} onpress={onlaunch} />
    </div>
  {/if}
  <Command why="Or in a terminal:" command={`$ loadout ${command.path}`} />
{:else if !command.runnable}
  <p class="calm">This groups the commands under it. Choose one of them.</p>
{:else}
  {#if asks}
    <p class="calm">It asks as it goes, so it opens in a terminal Studio hosts, where you answer it. Closing the page leaves it running.</p>
  {/if}
  <form class="cmdform" onsubmit={(event) => { event.preventDefault(); if (asks) { if (!notYet && !studio.watchOnly) void openTerminal(); } else if (!runReason) void run(false); }}>
    {#each shownArguments as argument (argument.name)}
      <label class="fld" for={id(argument.name)}>
        <span class="lab">{argument.name.toLowerCase()}{argument.required ? '' : ' (optional)'}</span>
        {#if argument.many}
          <textarea id={id(argument.name)} rows="3" bind:value={values[argument.name]} spellcheck="false"></textarea>
          <span class="calm small">One on each line. {argument.description}</span>
        {:else}
          <input id={id(argument.name)} bind:value={values[argument.name]} autocomplete="off" spellcheck="false" />
          {#if argument.description}<span class="calm small">{argument.description}</span>{/if}
        {/if}
      </label>
    {/each}

    {#snippet option(one: StudioOption)}
      {#if one.kind === 'flag'}
        <label class="chk"><input type="checkbox" id={id(one.name)} bind:checked={values[one.name]} /> <span><code class="ls-cmd">{one.name}</code> {one.description}</span></label>
      {:else if one.choices.length}
        <label class="fld" for={id(one.name)}>
          <span class="lab"><code class="ls-cmd">{one.name}</code>{one.required ? '' : ' (optional)'}</span>
          <select id={id(one.name)} bind:value={values[one.name]}>
            <option value="">not given</option>
            {#each one.choices as choice (choice)}<option value={choice}>{choice}</option>{/each}
          </select>
          <span class="calm small">{one.description}</span>
        </label>
      {:else}
        <label class="fld" for={id(one.name)}>
          <span class="lab"><code class="ls-cmd">{one.name}</code>{one.valueName ? ` ${one.valueName.toLowerCase()}` : ''}{one.required ? '' : ' (optional)'}</span>
          {#if one.kind === 'values'}
            <textarea id={id(one.name)} rows="2" bind:value={values[one.name]} spellcheck="false"></textarea>
            <span class="calm small">One on each line. {one.description}</span>
          {:else}
            <input id={id(one.name)} bind:value={values[one.name]} autocomplete="off" spellcheck="false" />
            <span class="calm small">{one.kind === 'optional' ? 'Leave it empty to give it with no value. ' : ''}{one.description}</span>
          {/if}
        </label>
      {/if}
    {/snippet}

    {#each own as one (one.name)}{@render option(one)}{/each}

    {#if everyCommand.length}
      <details class="more">
        <summary>Options every command has</summary>
        {#each everyCommand as one (one.name)}{@render option(one)}{/each}
      </details>
    {/if}

    <Command why="It runs:" command={lineFor(command.path, words)} live={drawer} />

    <div class="ls-answers flush">
      {#if asks}
        <HaloButton label={starting ? 'Opening…' : 'Open it in a terminal'} solid size="small" cmd="opens a terminal here, hosted by the daemon" disabledReason={notYet ?? (studio.watchOnly ? studio.watchOnlyReason : undefined)} onpress={() => void openTerminal()} />
      {:else}
        {#if previewable}
          <HaloButton label="Preview" size="small" cmd="adds --dry-run · changes nothing" disabledReason={notYet ?? undefined} onpress={() => void run(true)} />
        {/if}
        {#if drawer}
          <HaloButton label={starting ? 'Starting…' : label} solid={!reads && !runReason} kind={danger && !reads ? 'danger' : ''} size="small" cmd={runCmd} disabledReason={runReason} onpress={() => void run(false)} />
        {:else}
          <HaloButton label={starting ? 'Starting…' : label} solid={reads} kind={reads ? '' : 'needs'} size="small" cmd={runCmd} disabledReason={runReason} onpress={() => void run(false)} />
        {/if}
      {/if}
    </div>
  </form>

  {#if started}
    <JobResult id={started} compact />
  {/if}
{/if}
