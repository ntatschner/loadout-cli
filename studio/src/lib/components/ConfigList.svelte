<script lang="ts">
  // Every setting 'loadout config' knows, in the groups the terminal's
  // Settings screen uses: found by name or by what it does, and changed
  // where it is shown with 'loadout config set'. Empty clears a setting back
  // to its default.
  import HaloButton from './HaloButton.svelte';
  import Switch from './Switch.svelte';
  import { lineFor } from '../commandLine';
  import type { ConfigGroup, ConfigSetting } from '../api';

  interface Props {
    groups: ConfigGroup[];
    disabledReason?: string;
    onset?: (name: string, value: string) => void;
    /** What config get <name> --explain says. */
    onexplain?: (setting: ConfigSetting) => Promise<string>;
  }

  let { groups, disabledReason = undefined, onset = undefined, onexplain = undefined }: Props = $props();

  let query = $state('');
  let drafts = $state<Record<string, string>>({});
  let explained = $state<Record<string, string>>({});
  let opened = $state<Record<string, boolean>>({});
  let said = $state('');

  const find = $derived(query.trim().toLowerCase());
  const matches = (one: ConfigSetting) => !find || `${one.name} ${one.description}`.toLowerCase().includes(find);
  const shown = $derived(groups.map((group) => ({ ...group, matching: group.settings.filter(matches) })).filter((group) => group.matching.length));
  const total = $derived(groups.reduce((sum, group) => sum + group.settings.length, 0));
  const found = $derived(shown.reduce((sum, group) => sum + group.matching.length, 0));
  // A search opens every group with a match; otherwise the first is open until you choose.
  const isOpen = (group: string, index: number) => !!find || (opened[group] ?? index === 0);
  const changed = (one: ConfigSetting) => drafts[one.name] !== undefined && drafts[one.name] !== one.value;
  const valueOf = (one: ConfigSetting) => drafts[one.name] ?? one.value;
  // The descriptions come without a full stop; one is added so what it uses when unset reads as its own sentence.
  const sentence = (text: string) => (!text || /[.!?]$/.test(text.trim()) ? text : `${text.trim()}.`);

  function draft(name: string, value: string) {
    drafts = { ...drafts, [name]: value };
  }

  function putBack(name: string) {
    const { [name]: _, ...left } = drafts;
    drafts = left;
  }

  function set(one: ConfigSetting, value: string) {
    onset?.(one.name, value);
    putBack(one.name);
    said = `✓ ${value === '' ? `Cleared ${one.name}: it uses its default again` : `Set ${one.name} to ${value}`}. It runs as a job; the list reads again when it ends.`;
  }

  async function explain(one: ConfigSetting) {
    explained = { ...explained, [one.name]: onexplain ? await onexplain(one) : lineFor('config get', [one.name, '--explain']) };
  }
</script>

<div class="ls-config">
  <div class="find">
    <label class="ls-vh" for="cfg-find">Find a setting</label>
    <input id="cfg-find" name="config-find" class="ls-text" type="search" placeholder="Find a setting by name or what it does" autocomplete="off" spellcheck="false" bind:value={query} />
    <span class="n" aria-live="polite">{find ? `${found} of ${total} settings` : `${total} settings`}</span>
  </div>
  <div class="groups">
    {#each shown as group, index (group.group)}
      {@const setCount = group.settings.filter((one) => one.value).length}
      <details class="grp" open={isOpen(group.group, index)} ontoggle={(event) => { if (!find) opened = { ...opened, [group.group]: (event.currentTarget as HTMLDetailsElement).open }; }}>
        <summary><h3>{group.group}</h3><span class="n">{find ? `${group.matching.length} of ${group.settings.length}` : group.settings.length}</span>{#if setCount}<span class="set">{setCount} set</span>{/if}</summary>
        <div class="rows">
          {#each group.matching as one (one.name)}
            <div class={['crow', changed(one) ? 'changed' : ''].filter(Boolean).join(' ')} role="group" aria-label={one.name}>
              <div>
                <div class="k"><code>{one.name}</code>{#if one.machineLocal}<span class="ls-tag">this machine only</span>{/if}{#if one.value && !one.flag}<span class="ls-tag accent">set</span>{/if}</div>
                <p class="d">{sentence(one.description)}{#if !one.value && one.whenUnset}{' '}<span class="u">Unset, it uses {one.whenUnset}.</span>{/if}</p>
              </div>
              <div class="ctl">
                {#if one.flag}
                  <Switch label={one.name} bind:checked={() => valueOf(one) === 'true', (on) => draft(one.name, on ? 'true' : 'false')} />
                {:else}
                  <input class="ls-text" name={`config-${one.name}`} aria-label={one.name} value={valueOf(one)} placeholder={one.whenUnset ? `default: ${one.whenUnset}` : 'unset'} autocomplete="off" spellcheck="false" oninput={(event) => draft(one.name, (event.currentTarget as HTMLInputElement).value)} />
                {/if}
                <div class="acts">
                  {#if changed(one)}
                    <HaloButton label="Set it" solid={!disabledReason} size="small" cmd={lineFor('config set', [one.name, drafts[one.name]])} {disabledReason} onpress={() => set(one, drafts[one.name])} />
                    <HaloButton label="Put it back" size="small" cmd="changes nothing" onpress={() => putBack(one.name)} />
                  {:else if one.value && !one.flag}
                    <HaloButton label="Clear it" size="small" cmd={lineFor('config set', [one.name, ''])} {disabledReason} onpress={() => set(one, '')} />
                  {/if}
                  <button type="button" class="xb" title={lineFor('config get', [one.name, '--explain'])} aria-label={`Explain ${one.name}: ${lineFor('config get', [one.name, '--explain'])}`} onclick={() => void explain(one)}>Explain it</button>
                </div>
              </div>
              <p class="ex" aria-live="polite">{explained[one.name] ?? ''}</p>
            </div>
          {/each}
        </div>
      </details>
    {:else}
      <p class="none">No setting matches “{query.trim()}”.</p>
    {/each}
  </div>
  <p class="ls-said" role="status">{said}</p>
  <p class="ls-from">The same as <code>$ loadout config list</code>. Empty clears a setting back to its default.</p>
</div>
