<script lang="ts">
  // Every setting 'loadout config' knows, in the groups the terminal's
  // Settings screen uses, each changed where it is shown by running
  // 'loadout config set'. Empty clears it back to its default.
  import { untrack } from 'svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import { commands, configuration, SignedOut, type ConfigGroup } from '../lib/api';
  import { lineFor } from '../lib/commandLine';
  import { studio } from '../lib/state.svelte';

  let groups = $state<ConfigGroup[] | null>(null);
  let drafts = $state<Record<string, string>>({});
  let find = $state('');
  let problem = $state<string | null>(null);

  async function load() {
    try {
      groups = await configuration();
    } catch (error) {
      if (error instanceof SignedOut) {
        studio.signedOut = true;
      } else {
        problem = (error as Error).message;
      }
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
      untrack(() => {
        drafts = {};
        void load();
      });
    }
  });

  async function set(name: string, value: string) {
    try {
      await commands.run('config set', [name, value]);
      await studio.loadJobs();
    } catch (error) {
      problem = (error as Error).message;
    }
  }

  const shown = $derived(
    (groups ?? [])
      .map((group) => ({
        ...group,
        settings: group.settings.filter((one) => !find.trim() || `${one.name} ${one.description}`.toLowerCase().includes(find.trim().toLowerCase())),
      }))
      .filter((group) => group.settings.length),
  );

  const draft = (name: string, value: string) => drafts[name] ?? value;
  const changed = (name: string, value: string) => drafts[name] !== undefined && drafts[name] !== value;
</script>

{#if problem}<p class="offline" role="alert">{problem}</p>{/if}

<label class="find"><span class="ls-sr">Find a setting</span><input type="search" name="find-setting" placeholder="Find a setting" bind:value={find} autocomplete="off" /></label>

{#if groups === null}
  <p class="calm" role="status">Reading…</p>
{/if}

{#each shown as group (group.group)}
  <details class="store fold" open={find.trim().length > 0}>
    <summary><h3 class="ls-zone">{group.group} <span class="count">{group.settings.length}</span></h3></summary>
    <ul class="configs">
      {#each group.settings as one (one.name)}
        <li class="config">
          <div class="what">
            <code class="ls-cmd">{one.name}</code>{one.machineLocal ? ' · this machine only' : ''}
            <p class="calm small">{one.description}{!one.value && one.whenUnset ? ` Unset, it uses ${one.whenUnset}.` : ''}</p>
          </div>
          <div class="how">
            {#if one.flag}
              <label class="chk"><input type="checkbox" name={`set-${one.name}`} checked={draft(one.name, one.value) === 'true'} onchange={(event) => (drafts[one.name] = String((event.currentTarget as HTMLInputElement).checked))} /> <span>{draft(one.name, one.value) === 'true' ? 'on' : 'off'}</span></label>
            {:else}
              <label><span class="ls-sr">{one.name}</span><input name={`set-${one.name}`} value={draft(one.name, one.value)} placeholder={one.whenUnset ? 'default' : 'unset'} oninput={(event) => (drafts[one.name] = (event.currentTarget as HTMLInputElement).value)} autocomplete="off" spellcheck="false" /></label>
            {/if}
            {#if changed(one.name, one.value)}
              <HaloButton label="Set it" size="small" kind="needs" cmd={lineFor('config set', [one.name, drafts[one.name]])} disabledReason={studio.watchOnly ? studio.watchOnlyReason : undefined} onpress={() => void set(one.name, drafts[one.name])} />
            {/if}
          </div>
        </li>
      {/each}
    </ul>
  </details>
{/each}

<p class="calm small">The same as <code class="ls-cmd">loadout config list</code>. One setting with what it does when unset is <code class="ls-cmd">loadout config get &lt;key&gt; --explain</code>; clearing one sets it to nothing.</p>

<div class="acts">
  <CommandAction path="config get" title="One setting, explained" preset={{ '--explain': true }} label="Explain it" />
  <CommandAction path="config edit" title="The configuration file itself" preset={{ '--path-only': true }} label="Show where" />
</div>
