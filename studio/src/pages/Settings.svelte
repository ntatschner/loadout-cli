<script lang="ts">
  // Settings: a section index beside one section. Studio's own settings,
  // Loadout's settings as a searchable list, agents and packs, backups and
  // safety, the daemon, and how this machine stands. What is only ever done
  // in a terminal gets its command and why, never a button.
  import { untrack } from 'svelte';
  import CommandAction from '../lib/components/CommandAction.svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import HealthList from '../lib/components/HealthList.svelte';
  import Signal from '../lib/components/Signal.svelte';
  import Switch from '../lib/components/Switch.svelte';
  import TerminalOnly from '../lib/components/TerminalOnly.svelte';
  import Appearance from './Appearance.svelte';
  import SettingsLoadout from './SettingsLoadout.svelte';
  import { commands, readJson, signOut, SignedOut, teamApi } from '../lib/api';
  import { lineFor } from '../lib/commandLine';
  import { command } from '../lib/commands';
  import { studio } from '../lib/state.svelte';
  import { toasts } from '../lib/toasts.svelte';

  /** focused: just the section, for one opened on its own in Minimal. */
  let { rest = [], focused = false }: { rest?: string[]; focused?: boolean } = $props();

  // #settings/<section>; Studio's own settings when none is named.
  const SECTIONS = [
    { key: 'studio', label: 'Studio', sub: 'how it looks, keys, signing in' },
    { key: 'loadout', label: "Loadout's settings", sub: 'every loadout config setting' },
    { key: 'agents', label: 'Agents and packs', sub: 'MCP servers, secrets, packs' },
    { key: 'safety', label: 'Backups and safety', sub: 'backups, checkpoints, protections' },
    { key: 'daemon', label: 'The daemon', sub: 'running, starts at log-in' },
    { key: 'machine', label: 'This machine', sub: 'how it stands' },
  ];
  const section = $derived(SECTIONS.some((one) => one.key === rest[0]) ? rest[0] : 'studio');

  /**
   * Why tools verify is never passed --agree from a page, word for word as
   * StudioCoverage.Refused gives it: the catalogue carries a command's own
   * reason but not a refused option's.
   */
  const AGREE_REASON = "agreeing to a tool's tests is made in a terminal on purpose";

  interface Pack { name: string; remote: string; ref?: string; pinned?: string; approved?: boolean }
  interface Backup { id: string; operation?: string; detail?: string; created?: string; files?: number; creates?: number }
  interface Check { category?: string; name: string; severity: string; detail: string; fixable?: boolean }

  let packs = $state<Pack[] | null>(null);
  let mcp = $state<{ servers?: { name: string; type?: string; reaches?: string; scope?: string }[] } | null>(null);
  let backups = $state<Backup[] | null>(null);
  let autostart = $state<{ enabled?: boolean; entry?: string; command?: string; oldName?: boolean } | null>(null);
  let daemon = $state<{ daemon: boolean; paused: boolean | null } | null>(null);
  let doctor = $state<{ verdict?: string; checks: Check[] } | null>(null);
  let asking = $state<'restart' | 'stop' | null>(null);
  let updating = $state<'install' | 'roll-back' | null>(null);
  let checking = $state(false);
  let signingOut = $state(false);
  let idle = $state(String(studio.prefs?.idleAfter ?? 10));

  function quietly<T>(path: string, words: string[], into: (value: T) => void) {
    readJson<T>(path, words).then(into, () => undefined);
  }

  function load(which: string) {
    if (which === 'agents') {
      quietly<Pack[]>('pack list', [], (read) => (packs = read));
      quietly<NonNullable<typeof mcp>>('mcp list', ['--global'], (read) => (mcp = read));
    } else if (which === 'safety') {
      quietly<{ sets?: Backup[] } | Backup[]>('backup list', [], (read) => (backups = Array.isArray(read) ? read : (read.sets ?? [])));
    } else if (which === 'daemon') {
      quietly<NonNullable<typeof autostart>>('daemon autostart show', [], (read) => (autostart = read));
      teamApi.daemon().then((read) => (daemon = read), () => (daemon = null));
      void studio.loadUpdate();
    } else if (which === 'machine') {
      quietly<NonNullable<typeof doctor>>('doctor', [], (read) => (doctor = read));
    }
  }

  $effect(() => {
    const which = section;

    untrack(() => load(which));
  });

  // The daemon's autostart is a live fact worth putting in the index once it is known.
  $effect(() => {
    untrack(() => quietly<NonNullable<typeof autostart>>('daemon autostart show', [], (read) => (autostart = read)));
  });

  const settled = $derived(studio.jobs.filter((job) => job.state !== 'running').length);
  let seen = untrack(() => settled);

  $effect(() => {
    if (settled !== seen) {
      seen = settled;
      untrack(() => load(section));
    }
  });

  async function act(path: string, words: string[] = []) {
    if (studio.watchOnly) {
      toasts.add({ level: 'info', text: 'Studio is only watching, so nothing was changed.', cmd: lineFor(path, words) });

      return;
    }

    try {
      await commands.run(path, words);
      await studio.loadJobs();
    } catch (error) {
      if (error instanceof SignedOut) {
        studio.signedOut = true;
      } else {
        toasts.add({ level: 'problem', text: (error as Error).message, cmd: lineFor(path, words) });
      }
    }
  }

  /** Reads the release feed now, through the daemon, rather than waiting for its daily look. */
  async function checkNow() {
    checking = true;

    try {
      await studio.loadUpdate(true);
    } catch (error) {
      toasts.add({ level: 'problem', text: (error as Error).message, cmd: lineFor('update', ['--check']) });
    } finally {
      checking = false;
    }
  }

  /** Installs or rolls back through 'loadout update', as typing it would, then shows it pending. */
  async function upgrade(words: string[]) {
    await act('update', words);
    await studio.loadUpdate();
  }

  async function out() {
    try {
      await signOut();
      studio.signedOut = true;
    } catch (error) {
      toasts.add({ level: 'problem', text: (error as Error).message, cmd: command.signOut() });
    }
  }

  const refused = $derived(studio.watchOnly ? studio.watchOnlyReason : undefined);
  const reasonOf = (path: string) => {
    const reason = studio.command(path)?.reach.reason;

    return reason ? `It ${reason}.` : undefined;
  };
  const subOf = (key: string, sub: string) =>
    key === 'daemon' && studio.update?.pending
      ? 'an update is pending'
      : key === 'daemon' && studio.update?.isNewer
        ? `${studio.update.available} is available`
        : key === 'daemon' && autostart
          ? (autostart.enabled ? 'starts when you log in' : 'does not start at log-in')
          : sub;
  const upd = $derived(studio.update);
  const plural = (count: number, one: string) => `${count} ${one}${count === 1 ? '' : 's'}`;
  /** What a restart straight away ends, in words, or empty when it ends nothing. */
  const cutOff = $derived(
    upd ? [upd.runs ? `the ${plural(upd.runs, 'run')} it started` : '', upd.hosted ? `the ${plural(upd.hosted, 'session')} it hosts` : ''].filter(Boolean).join(' and ') : '',
  );
  const updateWords = $derived(updating === 'roll-back' ? ['--roll-back', '--restart-daemon', '--yes'] : ['--restart-daemon', '--yes']);
  const linkish = (text: string) => /^https?:\/\/\S+$/.test(text.trim());
  const held = $derived(daemon?.paused ?? null);
  const checks = $derived((doctor?.checks ?? []).map((one) => ({ name: one.name, severity: one.severity, detail: one.detail, fixable: one.fixable })));
  const when = (at: string | undefined) => (at ? new Date(at).toLocaleString([], { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '');

  $effect(() => {
    void studio.loadCatalogue().catch(() => undefined);
  });
</script>

{#if !focused}<div class="head"><h1>Settings</h1></div>{/if}

<div class={['setwrap', focused ? 'solo' : ''].filter(Boolean).join(' ')}>
  {#if !focused}
  <nav class="setidx" aria-label="Settings">
    <ul>
      {#each SECTIONS as one (one.key)}
        <li><a href={`#settings/${one.key}`} aria-current={section === one.key ? 'page' : undefined}><span class="t">{one.label}</span><span class="x">{subOf(one.key, one.sub)}</span></a></li>
      {/each}
    </ul>
  </nav>
  {/if}

  <div class="setpane">
    {#if section === 'studio'}
      <section class="pblock" aria-labelledby="z-app">
        <h2 class="ls-zone" id="z-app">Appearance</h2>
        <Appearance />
      </section>

      <section class="pblock" aria-labelledby="z-lay">
        <h2 class="ls-zone" id="z-lay">Layouts</h2>
        <p class="lead">Home's Lens and Map are edited in place, from Edit layout on Home. These put them back as they came.</p>
        <div class="ls-answers flush">
          <HaloButton label="Reset the Lens" size="small" cmd={command.setting('studio-layout-lens', '')} disabledReason={studio.watchOnly ? `Studio is only watching · ${command.setting('studio-layout-lens', '')}` : undefined} onpress={() => void studio.setPreference('studio-layout-lens', '')} />
          <HaloButton label="Reset the Map" size="small" cmd={command.setting('studio-layout-map', '')} disabledReason={studio.watchOnly ? `Studio is only watching · ${command.setting('studio-layout-map', '')}` : undefined} onpress={() => void studio.setPreference('studio-layout-map', '')} />
        </div>
      </section>

      <section class="pblock" aria-labelledby="z-amb">
        <h2 class="ls-zone" id="z-amb">Ambient and keys</h2>
        <Switch label="Single-letter shortcuts" sub="A for Ambient, N for a new session · kept in this browser" checked={studio.keys} onchange={(on) => studio.setKeys(on)} />
        <Switch label="Ambient fills the screen" sub="kept in this browser" checked={studio.fullscreen} onchange={(on) => studio.setFullscreen(on)} />
        <div class="ls-field">
          <label class="lab" for="idle-after">A quiet session reads idle after, in minutes</label>
          <div class="idlerow">
            <input id="idle-after" type="number" min="1" max="1440" inputmode="numeric" bind:value={idle} disabled={studio.watchOnly} onchange={() => void studio.setPreference('studio-idle-after', String(idle))} />
            <code class="ls-cmd">{command.setting('studio-idle-after', String(idle))}</code>
          </div>
        </div>
      </section>

      <section class="pblock" aria-labelledby="z-sign">
        <h2 class="ls-zone" id="z-sign">Signing in</h2>
        <p class="lead">This browser is signed in to Studio on port {studio.about?.port ?? '…'}. Signing out signs out every browser on this machine; open Studio again with <code class="ls-cmd">{command.open()}</code>.</p>
        <HaloButton label="Sign out every browser" kind="danger" size="small" cmd={command.signOut()} disabledReason={studio.watchOnly ? `Studio is only watching · ${command.signOut()}` : undefined} onpress={() => (signingOut = true)} />
      </section>

      <section class="pblock" aria-labelledby="z-term">
        <h2 class="ls-zone" id="z-term">Done in a terminal</h2>
        <p class="lead">These are only ever done where you type, so that nothing a page can press reads a secret back or makes an agreement for you.</p>
        <TerminalOnly title="The passphrase for typing at a team's node" command="$ loadout team attach set" why={reasonOf('team attach set')} fact={`Passphrase: ${studio.about?.attachPassphrase ? 'set' : 'not set'}`} />
        <TerminalOnly title="Agree to a tool's tests running on this machine" command="$ loadout tools verify <tool> --agree" why={`It is ${AGREE_REASON}.`} />
        <TerminalOnly title="Store a secret" command="$ loadout secret set" why={reasonOf('secret set')} />
      </section>

      <section class="pblock" aria-labelledby="z-about">
        <h2 class="ls-zone" id="z-about">About</h2>
        <dl class="facts">
          <dt>Version</dt><dd>{studio.about?.version ?? '…'}</dd>
          <dt>Platform</dt><dd>{studio.about?.platform ?? '…'}</dd>
          <dt>Terminal</dt><dd>{studio.about?.pseudoTerminal ?? 'not available'}</dd>
        </dl>
      </section>
    {:else if section === 'loadout'}
      <section class="pblock" aria-labelledby="z-cfg">
        <h2 class="ls-zone" id="z-cfg">Loadout's settings</h2>
        <SettingsLoadout />
      </section>
    {:else if section === 'agents'}
      <section class="pblock" aria-labelledby="z-mcp">
        <h2 class="ls-zone" id="z-mcp">MCP servers</h2>
        <p class="lead">The servers every project's agent loads. A project's own are on its Context page.</p>
        {#if mcp?.servers?.length}
          <ul class="rowsx">{#each mcp.servers as one (one.name)}<li><b>{one.name}</b>{#if one.type}<span>{one.type}</span>{/if}<code>{one.reaches ?? ''}</code>{#if one.scope}<span class="ls-tag">{one.scope}</span>{/if}</li>{/each}</ul>
        {:else if mcp}
          <p class="lead">None for every project.</p>
        {/if}
        <p class="ls-from">From <code>$ loadout mcp list --global</code>.</p>
        <div class="ls-drawers">
          <CommandAction path="mcp add" title="Add a server" />
          <CommandAction path="mcp remove" title="Remove one" label="Remove" danger />
        </div>
      </section>

      <section class="pblock" aria-labelledby="z-sec">
        <h2 class="ls-zone" id="z-sec">Secrets</h2>
        <p class="lead">Kept in this machine's credential store. Studio never shows one back.</p>
        <TerminalOnly title="Store a secret" command="$ loadout secret set" why={reasonOf('secret set')} />
        <div class="ls-drawers">
          <CommandAction path="secret test" title="Check a reference resolves" label="Check it" />
          <CommandAction path="secret remove" title="Remove one" label="Remove" danger />
        </div>
      </section>

      <section class="pblock" aria-labelledby="z-pack">
        <h2 class="ls-zone" id="z-pack">Specialist packs</h2>
        {#if packs?.length}
          <ul class="rowsx">{#each packs as one (one.name)}<li><b>{one.name}</b><code>{one.remote}{one.ref ? ` @ ${one.ref}` : ''}</code><span class={`ls-tag ${one.approved ? 'good' : 'needs'}`}>{one.approved ? 'approved here' : 'not approved here'}</span></li>{/each}</ul>
        {:else if packs}
          <p class="lead">No packs declared.</p>
        {/if}
        <p class="ls-from">From <code>$ loadout pack list</code>. Fetching a pack is not approving it: approve one only once you have read it.</p>
        <div class="ls-drawers">
          <CommandAction path="pack add" title="Declare a pack" />
          <CommandAction path="pack approve" title="Approve a pack's pinned commit here" label="Approve" />
          <CommandAction path="pack update" title="Move a pack's pin to what its ref points at" label="Update" />
          <CommandAction path="pack remove" title="Stop declaring one" label="Remove" danger />
        </div>
      </section>
    {:else if section === 'safety'}
      <section class="pblock" aria-labelledby="z-back">
        <h2 class="ls-zone" id="z-back">Backups</h2>
        <p class="lead">Taken before anything that changes many files at once, so it can be undone.</p>
        {#if backups?.length}
          <ul class="rowsx">
            {#each backups.slice(0, 20) as one (one.id)}
              <li><code>{one.id}</code><b>{one.operation ?? ''}</b>{#if one.detail}<span>{one.detail}</span>{/if}<span>{when(one.created)}</span>{#if one.files !== undefined}<span>{one.files} file{one.files === 1 ? '' : 's'}{one.creates ? `, ${one.creates} created` : ''}</span>{/if}</li>
            {/each}
          </ul>
          {#if backups.length > 20}<p class="ls-from">The {backups.length - 20} oldest are left out here.</p>{/if}
        {:else if backups}
          <p class="lead">No backups yet.</p>
        {/if}
        <p class="ls-from">From <code>$ loadout backup list</code>.</p>
        <div class="ls-drawers">
          <CommandAction path="backup restore" title="Undo what one was taken for" label="Restore" note="It only reports what it would put back until --apply is ticked." />
        </div>
      </section>

      <section class="pblock" aria-labelledby="z-check">
        <h2 class="ls-zone" id="z-check">Checkpoints</h2>
        <p class="lead">A name for where a project stands, to come back to.</p>
        <div class="ls-drawers">
          <CommandAction path="checkpoint list" title="A project's checkpoints" label="Show" />
          <CommandAction path="checkpoint create" title="Mark where a project stands" label="Mark it" />
          <CommandAction path="checkpoint restore" title="Put a checkpoint's files back" label="Restore" />
          <CommandAction path="checkpoint remove" title="Forget one" label="Forget it" danger />
        </div>
      </section>

      <section class="pblock" aria-labelledby="z-prot">
        <h2 class="ls-zone" id="z-prot">Keeping AI files out of repositories</h2>
        <div class="ls-drawers">
          <CommandAction path="protect" title="Install the Git protections" />
          <CommandAction path="migrate" title="Move existing AI tooling files into the workspace" />
        </div>
      </section>
    {:else if section === 'daemon'}
      <section class="pblock" aria-labelledby="z-dae">
        <h2 class="ls-zone" id="z-dae">The daemon</h2>
        <div class="dstat">
          <Signal word={held ? 'held' : 'running'} big />
          <div>
            <b>{held ? 'Running, with its schedules held' : held === false ? 'Running, its schedules free to fire' : 'Running'}</b>
            <p>The daemon serves this page, hosts its sessions and fires scheduled team runs. Stopping or restarting it ends the sessions it hosts.</p>
            {#if autostart}
              <p class="x">{autostart.enabled ? 'It starts when you log in' : 'It does not start when you log in'}{autostart.entry ? `, from ${autostart.entry}` : ''}.{autostart.oldName ? ' The entry uses the old name, team daemon; enabling it again rewrites it.' : ''}</p>
            {/if}
          </div>
        </div>
        <p class="ls-from">From <code>$ loadout daemon autostart show</code>; whether the schedules are held, from the daemon itself.</p>
        <div class="ls-answers flush">
          {#if held !== true}
            <HaloButton label="Hold the schedules" size="small" cmd={lineFor('daemon pause', [])} disabledReason={refused} onpress={() => void act('daemon pause')} />
          {/if}
          {#if held !== false}
            <HaloButton label="Let the schedules fire" size="small" cmd={lineFor('daemon resume', [])} disabledReason={refused} onpress={() => void act('daemon resume')} />
          {/if}
          <HaloButton label="Restart it" size="small" cmd={lineFor('daemon restart', [])} disabledReason={refused} onpress={() => (asking = 'restart')} />
          <HaloButton label="Stop it" kind="danger" size="small" cmd={lineFor('daemon stop', [])} disabledReason={refused} onpress={() => (asking = 'stop')} />
          {#if autostart?.enabled}
            <HaloButton label="Stop it starting at log-in" size="small" cmd={lineFor('daemon autostart disable', [])} disabledReason={refused} onpress={() => void act('daemon autostart disable')} />
          {:else}
            <HaloButton label="Start it at log-in" size="small" cmd={lineFor('daemon autostart enable', [])} disabledReason={refused} onpress={() => void act('daemon autostart enable')} />
          {/if}
        </div>
        <div class="ls-drawers">
          <CommandAction path="daemon continue" title="Let the schedules fire, by its other name" label="Let them fire" />
        </div>
      </section>

      <section class="pblock" aria-labelledby="z-upd">
        <h2 class="ls-zone" id="z-upd">Version and updates</h2>
        {#if upd}
          <p class="vline">
            <b>{upd.current ?? studio.about?.version}</b>
            {#if upd.isNewer}<span>, and <b>{upd.available}</b> is available.</span>{:else if upd.available}<span>, the newest release.</span>{/if}
          </p>
          {#if upd.pending}
            <p class="updnote" role="status">
              Update pending: {upd.pending.described}.
              {upd.pending.now ? 'The daemon is restarting now.' : upd.runs ? `It restarts once ${upd.runs === 1 ? 'the run it started has' : `the ${upd.runs} runs it started have`} finished, and starts nothing new meanwhile.` : 'The daemon is restarting.'}
              This page comes back when it does.
            </p>
          {/if}
          {#if upd.isNewer && upd.notes}
            {#if linkish(upd.notes)}
              <p><a href={upd.notes.trim()} target="_blank" rel="noopener">What is new in {upd.available}</a></p>
            {:else}
              <details class="notes"><summary>What is new in {upd.available}</summary><p>{upd.notes}</p></details>
            {/if}
          {/if}
          {#if upd.problem}
            <p class="updnote">The last look at the release feed did not work: {upd.problem}</p>
          {/if}
          <p class="ls-from">
            {upd.checkedAt ? `Looked at the release feed ${when(upd.checkedAt)}` : 'Not looked at the release feed yet'}{upd.automatic ? '; the daemon looks once a day.' : '; the daemon does not look by itself, because updates-check-automatically is off.'}
            {upd.channel === 'installer'
              ? 'This copy was installed by the MSI, so an update runs the newer MSI while the daemon is down.'
              : 'This copy was unpacked from a release archive, so an update puts the new binary in place and keeps the old one for Roll back.'}
          </p>
          <div class="ls-answers flush">
            <HaloButton label={checking ? 'Looking…' : 'Check now'} size="small" cmd={lineFor('update', ['--check'])} disabledReason={checking ? 'Already looking' : undefined} onpress={() => void checkNow()} />
            {#if upd.isNewer && !upd.pending}
              <HaloButton label="Install and restart" kind="needs" size="small" cmd={lineFor('update', ['--restart-daemon', '--yes'])} disabledReason={refused} onpress={() => (updating = 'install')} />
            {/if}
            {#if upd.canRollBack && !upd.pending}
              <HaloButton label="Roll back" size="small" cmd={lineFor('update', ['--roll-back', '--restart-daemon', '--yes'])} disabledReason={refused} onpress={() => (updating = 'roll-back')} />
            {/if}
          </div>
        {:else}
          <p class="calm">This server does not look for updates. In a terminal: <code>loadout update</code></p>
        {/if}
      </section>
    {:else if section === 'machine'}
      <section class="pblock" aria-labelledby="z-doc">
        <h2 class="ls-zone" id="z-doc">How this machine stands</h2>
        {#if doctor}
          <HealthList findings={checks} verdict={doctor.verdict} from="$ loadout doctor" />
        {:else}
          <p class="calm" role="status">Checking…</p>
        {/if}
        <div class="ls-drawers">
          {#if checks.some((one) => one.fixable)}
            <CommandAction path="doctor" title="Put right what can be put right" preset={{ '--fix': true, '--yes': true }} label="Put it right" />
          {/if}
          <CommandAction path="setup" title="Set the launcher up on this machine again" />
          <CommandAction path="desktop" title="The desktop entry for the launcher" />
          <CommandAction path="commands" title="Everything Loadout can do, as the terminal lists it" label="List them" />
        </div>
        <p class="ls-from">Every command also has a form on the <a href="#commands">Commands</a> page.</p>
      </section>
    {/if}
  </div>
</div>

{#if asking}
  <Dialog
    title={asking === 'restart' ? 'Restart the daemon?' : 'Stop the daemon?'}
    why={asking === 'restart'
      ? 'It ends the sessions the daemon hosts, then starts again with the same settings once the runs it started have finished. This page comes back when it does.'
      : 'It ends the sessions the daemon hosts once the runs it started have finished. This page stops answering until the daemon is started again.'}
    danger={asking === 'stop'}
    actions={[{ label: asking === 'restart' ? 'Restart it' : 'Stop it', kind: asking === 'stop' ? 'danger' : '', cmd: lineFor(asking === 'restart' ? 'daemon restart' : 'daemon stop', []), onpress: () => void act(asking === 'restart' ? 'daemon restart' : 'daemon stop') }]}
    onclose={() => (asking = null)}
  />
{/if}

{#if updating && upd}
  <Dialog
    title={updating === 'install' ? `Install ${upd.available} and restart the daemon?` : 'Roll back and restart the daemon?'}
    why={(updating === 'install'
      ? `The daemon restarts on ${upd.available}${upd.channel === 'installer' ? ', running the MSI while it is down' : ', and if that version does not start, the one before is put back'}.`
      : 'The daemon restarts on the version before this one.')
      + (cutOff
        ? ` Waiting lets ${cutOff} carry on until the runs have finished, and nothing new starts meanwhile; restarting now ends ${cutOff} at once.`
        : ' Nothing is running that a restart would end.')
      + ' This page comes back when the daemon does.'}
    actions={[
      { label: 'Restart when its runs finish', cmd: lineFor('update', updateWords), onpress: () => void upgrade(updateWords) },
      { label: 'Restart now', kind: 'danger', cmd: lineFor('update', [...updateWords, '--now']), onpress: () => void upgrade([...updateWords, '--now']) },
    ]}
    onclose={() => (updating = null)}
  />
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
