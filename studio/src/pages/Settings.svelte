<script lang="ts">
  // Studio's settings: how it looks, its layouts, Ambient and keys, signing
  // in, and the things that are only ever done in a terminal, which get their
  // command and never a button.
  import Command from '../lib/components/Command.svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import Switch from '../lib/components/Switch.svelte';
  import Appearance from './Appearance.svelte';
  import SettingsLoadout from './SettingsLoadout.svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import { readJson, signOut } from '../lib/api';
  import { command } from '../lib/commands';
  import { studio } from '../lib/state.svelte';
  import { toasts } from '../lib/toasts.svelte';

  let { rest = [] }: { rest?: string[] } = $props();

  // #settings/<section>; Studio's own settings when none is named.
  const SECTIONS = [
    { key: 'studio', label: 'Studio' },
    { key: 'loadout', label: "Loadout's settings" },
    { key: 'agents', label: 'Agents and packs' },
    { key: 'safety', label: 'Backups and safety' },
    { key: 'daemon', label: 'The daemon' },
    { key: 'machine', label: 'This machine' },
  ];
  const section = $derived(SECTIONS.some((one) => one.key === rest[0]) ? rest[0] : 'studio');

  interface Pack { name: string; remote: string; ref?: string; pinned?: string; approved?: boolean }
  interface Backups { sets?: { id: string; operation?: string; created?: string; files?: number }[] }

  let packs = $state<Pack[] | null>(null);
  let mcp = $state<{ servers?: { name: string; type?: string; reaches?: string; scope?: string }[]; clashes?: unknown[] } | null>(null);
  let backups = $state<unknown | null>(null);
  let autostart = $state<{ enabled?: boolean; entry?: string; command?: string; oldName?: boolean } | null>(null);
  let doctor = $state<{ checks: { name: string; severity: string; detail: string }[] } | null>(null);

  function quietly<T>(path: string, words: string[], into: (value: T) => void) {
    readJson<T>(path, words).then(into, () => undefined);
  }

  $effect(() => {
    if (section === 'agents') {
      quietly<Pack[]>('pack list', [], (read) => (packs = read));
      quietly<typeof mcp & object>('mcp list', ['--global'], (read) => (mcp = read));
    } else if (section === 'safety') {
      quietly<unknown>('backup list', [], (read) => (backups = read));
    } else if (section === 'daemon') {
      quietly<typeof autostart & object>('daemon autostart show', [], (read) => (autostart = read));
    } else if (section === 'machine') {
      quietly<{ checks: { name: string; severity: string; detail: string }[] }>('doctor', [], (read) => (doctor = read));
    }
  });

  const backupRows = $derived(Array.isArray(backups) ? (backups as Record<string, unknown>[]) : (((backups as Backups | null)?.sets ?? []) as unknown as Record<string, unknown>[]));

  let signingOut = $state(false);
  let idle = $state(String(studio.prefs?.idleAfter ?? 10));

  async function out() {
    try {
      await signOut();
      studio.signedOut = true;
    } catch (error) {
      toasts.add({ level: 'problem', text: (error as Error).message, cmd: command.signOut() });
    }
  }
</script>

<div class="head"><h1>Settings</h1></div>

<nav class="subnav" aria-label="Settings">
  {#each SECTIONS as one (one.key)}
    <a href={`#settings/${one.key}`} aria-current={section === one.key ? 'page' : undefined}>{one.label}</a>
  {/each}
</nav>

{#if section === 'studio'}
<div class="settings">
  <section aria-labelledby="z-app">
    <h2 class="ls-zone" id="z-app">Appearance</h2>
    <Appearance />
  </section>

  <section aria-labelledby="z-lay">
    <h2 class="ls-zone" id="z-lay">Layouts</h2>
    <p class="calm">Home's Lens and Map are edited in place, from Edit layout on Home. These put them back as they came.</p>
    <div class="ls-answers flush">
      <HaloButton label="Reset the Lens" size="small" cmd={command.setting('studio-layout-lens', '')} disabledReason={studio.watchOnly ? `Studio is only watching · ${command.setting('studio-layout-lens', '')}` : undefined} onpress={() => void studio.setPreference('studio-layout-lens', '')} />
      <HaloButton label="Reset the Map" size="small" cmd={command.setting('studio-layout-map', '')} disabledReason={studio.watchOnly ? `Studio is only watching · ${command.setting('studio-layout-map', '')}` : undefined} onpress={() => void studio.setPreference('studio-layout-map', '')} />
    </div>
  </section>

  <section aria-labelledby="z-amb">
    <h2 class="ls-zone" id="z-amb">Ambient and keys</h2>
    <Switch label="Single-letter shortcuts" sub="A for Ambient, N for a new session · kept in this browser" checked={studio.keys} onchange={(on) => studio.setKeys(on)} />
    <Switch label="Ambient fills the screen" sub="kept in this browser" checked={studio.fullscreen} onchange={(on) => studio.setFullscreen(on)} />
    <div class="ls-field">
      <label class="lab" for="idle-after">A quiet session reads idle after, in minutes</label>
      <div class="idlerow">
        <input
          id="idle-after"
          type="number"
          min="1"
          max="1440"
          inputmode="numeric"
          bind:value={idle}
          disabled={studio.watchOnly}
          onchange={() => void studio.setPreference('studio-idle-after', String(idle))}
        />
        <code class="ls-cmd">{command.setting('studio-idle-after', String(idle))}</code>
      </div>
    </div>
  </section>

  <section aria-labelledby="z-sign">
    <h2 class="ls-zone" id="z-sign">Signing in</h2>
    <p class="calm">This browser is signed in to Studio on port {studio.about?.port ?? '…'}. Signing out signs out every browser on this machine; open Studio again with <code class="ls-cmd">{command.open()}</code>.</p>
    <HaloButton label="Sign out every browser" kind="danger" size="small" cmd={command.signOut()} disabledReason={studio.watchOnly ? `Studio is only watching · ${command.signOut()}` : undefined} onpress={() => (signingOut = true)} />
  </section>

  <section aria-labelledby="z-term">
    <h2 class="ls-zone" id="z-term">Done in a terminal</h2>
    <p class="calm">These are only ever done where you type, so that nothing a page can press reads a secret back or makes an agreement for you.</p>
    <dl class="facts">
      <dt>Team attach passphrase</dt><dd>{studio.about?.attachPassphrase ? 'set' : 'not set'}</dd>
    </dl>
    <Command why="Set or change the passphrase for typing at a team's node:" command="$ loadout team attach set" />
    <Command why="Agree to a tool's tests running on this machine:" command="$ loadout tools verify <tool> --agree" />
  </section>

  <section aria-labelledby="z-about">
    <h2 class="ls-zone" id="z-about">About</h2>
    <dl class="facts">
      <dt>Version</dt><dd>{studio.about?.version ?? '…'}</dd>
      <dt>Platform</dt><dd>{studio.about?.platform ?? '…'}</dd>
      <dt>Terminal</dt><dd>{studio.about?.pseudoTerminal ?? 'not available'}</dd>
    </dl>
  </section>
</div>
{:else if section === 'loadout'}
<div class="settings">
  <section aria-labelledby="z-cfg">
    <h2 class="ls-zone" id="z-cfg">Loadout's settings</h2>
    <SettingsLoadout />
  </section>
</div>
{:else if section === 'agents'}
<div class="settings">
  <section aria-labelledby="z-mcp">
    <h2 class="ls-zone" id="z-mcp">MCP servers</h2>
    <p class="calm">The servers every project's agent loads. A project's own are on its Context page.</p>
    {#if mcp?.servers?.length}
      <ul class="plain">{#each mcp.servers as one (one.name)}<li><span class="n">{one.name}</span>{one.type ? ` · ${one.type}` : ''} · <code class="ls-cmd">{one.reaches ?? ''}</code>{one.scope ? ` · ${one.scope}` : ''}</li>{/each}</ul>
    {:else if mcp}
      <p class="calm">None for every project.</p>
    {/if}
    <p class="calm small">From <code class="ls-cmd">loadout mcp list --global</code>.</p>
    <div class="acts">
      <CommandAction path="mcp add" title="Add a server" />
      <CommandAction path="mcp remove" title="Remove one" label="Remove" />
    </div>
  </section>

  <section aria-labelledby="z-sec">
    <h2 class="ls-zone" id="z-sec">Secrets</h2>
    <p class="calm">Kept in this machine's credential store. Studio never shows one back.</p>
    <div class="acts">
      <CommandAction path="secret test" title="Check a reference resolves" label="Check it" />
      <CommandAction path="secret remove" title="Remove one" label="Remove" />
      <CommandAction path="secret set" title="Store one" />
    </div>
  </section>

  <section aria-labelledby="z-pack">
    <h2 class="ls-zone" id="z-pack">Specialist packs</h2>
    {#if packs?.length}
      <ul class="plain">{#each packs as one (one.name)}<li><span class="n">{one.name}</span> · <code class="ls-cmd">{one.remote}</code>{one.ref ? ` @ ${one.ref}` : ''} · {one.approved ? 'approved here' : 'not approved here'}</li>{/each}</ul>
    {:else if packs}
      <p class="calm">No packs declared.</p>
    {/if}
    <p class="calm small">From <code class="ls-cmd">loadout pack list</code>. Fetching a pack is not approving it: approve one only once you have read it.</p>
    <div class="acts">
      <CommandAction path="pack add" title="Declare a pack" />
      <CommandAction path="pack approve" title="Approve a pack's pinned commit here" label="Approve" />
      <CommandAction path="pack update" title="Move a pack's pin to what its ref points at" label="Update" />
      <CommandAction path="pack remove" title="Stop declaring one" label="Remove" />
    </div>
  </section>
</div>
{:else if section === 'safety'}
<div class="settings">
  <section aria-labelledby="z-back">
    <h2 class="ls-zone" id="z-back">Backups</h2>
    <p class="calm">Taken before anything that changes many files at once, so it can be undone.</p>
    {#if backupRows.length}
      <ul class="plain">{#each backupRows.slice(0, 20) as one, index (index)}<li>{Object.values(one).filter((value) => typeof value === 'string' || typeof value === 'number').slice(0, 4).join(' · ')}</li>{/each}</ul>
    {:else if backups}
      <p class="calm">No backups yet.</p>
    {/if}
    <p class="calm small">From <code class="ls-cmd">loadout backup list</code>.</p>
    <div class="acts">
      <CommandAction path="backup restore" title="Undo what one was taken for" label="Restore" />
    </div>
  </section>

  <section aria-labelledby="z-check">
    <h2 class="ls-zone" id="z-check">Checkpoints</h2>
    <p class="calm">A name for where a project stands, to come back to.</p>
    <div class="acts">
      <CommandAction path="checkpoint list" title="A project's checkpoints" label="Show" />
      <CommandAction path="checkpoint create" title="Mark where a project stands" label="Mark it" />
      <CommandAction path="checkpoint restore" title="Put a checkpoint's files back" label="Restore" />
      <CommandAction path="checkpoint remove" title="Forget one" label="Forget it" />
    </div>
  </section>

  <section aria-labelledby="z-prot">
    <h2 class="ls-zone" id="z-prot">Keeping AI files out of repositories</h2>
    <div class="acts">
      <CommandAction path="protect" title="Install the Git protections" />
      <CommandAction path="migrate" title="Move existing AI tooling files into the workspace" />
    </div>
  </section>
</div>
{:else if section === 'daemon'}
<div class="settings">
  <section aria-labelledby="z-dae">
    <h2 class="ls-zone" id="z-dae">The daemon</h2>
    <p class="calm">The daemon serves this page, hosts its sessions and fires scheduled team runs. Stopping or restarting it ends the sessions it hosts.</p>
    {#if autostart}<p class="calm">{autostart.enabled ? 'It starts when you log in' : 'It does not start when you log in'}{autostart.entry ? `, from ${autostart.entry}` : ''}.{autostart.oldName ? ' The entry uses the old name, team daemon; enabling it again rewrites it.' : ''}</p>{/if}
    <p class="calm small">From <code class="ls-cmd">loadout daemon autostart show</code>.</p>
    <div class="acts">
      <CommandAction path="daemon pause" title="Hold the schedules" label="Hold them" />
      <CommandAction path="daemon resume" title="Let the schedules fire again" label="Let them fire" />
      <CommandAction path="daemon continue" title="The same, by its other name" label="Let them fire" />
      <CommandAction path="daemon restart" title="Restart it with the same settings" label="Restart" />
      <CommandAction path="daemon stop" title="Stop it" label="Stop" />
      <CommandAction path="daemon autostart enable" title="Start it when you log in" label="Enable" />
      <CommandAction path="daemon autostart disable" title="Stop it starting when you log in" label="Disable" />
    </div>
  </section>
</div>
{:else if section === 'machine'}
<div class="settings">
  <section aria-labelledby="z-doc">
    <h2 class="ls-zone" id="z-doc">How this machine stands</h2>
    {#if doctor}
      <ul class="findings">
        {#each doctor.checks as check, index (index)}
          <li class={check.severity.toLowerCase()}><span class="sev">{check.severity}</span> <span class="n">{check.name}</span> · {check.detail}</li>
        {/each}
      </ul>
    {:else}
      <p class="calm" role="status">Checking…</p>
    {/if}
    <p class="calm small">From <code class="ls-cmd">loadout doctor</code>.</p>
    <div class="acts">
      <CommandAction path="doctor" title="Put right what can be put right" preset={{ '--fix': true, '--yes': true }} label="Put it right" />
      <CommandAction path="desktop" title="The desktop entry for the launcher" />
      <CommandAction path="commands" title="Everything Loadout can do, as the terminal lists it" label="List them" />
    </div>
    <p class="calm small">Every command also has a form on the <a href="#commands">Commands</a> page.</p>
  </section>
</div>
{/if}

{#if signingOut}
  <Dialog
    title="Sign every browser out?"
    why="Every browser signed in to Studio or the dashboard on this machine is signed out, this one included. Nothing else changes."
    danger
    actions={[{ label: 'Sign out', kind: 'danger', cmd: command.signOut(), onpress: () => void out() }]}
    onclose={() => (signingOut = false)}
  />
{/if}
