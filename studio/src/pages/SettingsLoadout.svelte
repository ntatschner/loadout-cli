<script lang="ts">
  // Every setting 'loadout config' knows, as a ConfigList: found by name or
  // what it does, set and cleared where it is shown by running 'loadout
  // config set' as a job, and explained by 'loadout config get --explain'.
  import { untrack } from 'svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import ConfigList from '../lib/components/ConfigList.svelte';
  import { commands, configuration, SignedOut, type ConfigGroup, type ConfigSetting } from '../lib/api';
  import { studio } from '../lib/state.svelte';

  let groups = $state<ConfigGroup[] | null>(null);
  let problem = $state<string | null>(null);

  function fail(error: unknown) {
    if (error instanceof SignedOut) {
      studio.signedOut = true;
    } else {
      problem = (error as Error).message;
    }
  }

  async function load() {
    try {
      groups = await configuration();
    } catch (error) {
      fail(error);
    }
  }

  $effect(() => {
    void load();
  });

  const settled = $derived(studio.jobs.filter((job) => job.state !== 'running').length);
  let seen = untrack(() => settled);

  $effect(() => {
    if (settled !== seen) {
      seen = settled;
      untrack(() => void load());
    }
  });

  async function set(name: string, value: string) {
    try {
      await commands.run('config set', [name, value]);
      await studio.loadJobs();
    } catch (error) {
      fail(error);
    }
  }

  /** What config get <name> --explain says, read and kept nowhere. */
  async function explain(one: ConfigSetting): Promise<string> {
    try {
      const read = await commands.read('config get', [one.name, '--explain']);

      return (read.output || read.error || '').trim();
    } catch (error) {
      return (error as Error).message;
    }
  }
</script>

{#if problem}<p class="offline" role="alert">{problem}</p>{/if}

{#if groups === null}
  <p class="calm" role="status">Reading…</p>
{:else}
  <ConfigList {groups} disabledReason={studio.watchOnly ? studio.watchOnlyReason : undefined} onset={(name, value) => void set(name, value)} onexplain={explain} />
{/if}

<div class="ls-drawers pblock">
  <CommandAction path="config edit" title="The configuration file itself" preset={{ '--path-only': true }} label="Show where" />
</div>
