<script lang="ts">
  // Studio's settings: how it looks, its layouts, Ambient and keys, signing
  // in, and the things that are only ever done in a terminal, which get their
  // command and never a button.
  import Command from '../lib/components/Command.svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import HaloButton from '../lib/components/HaloButton.svelte';
  import Switch from '../lib/components/Switch.svelte';
  import Appearance from './Appearance.svelte';
  import { signOut } from '../lib/api';
  import { command } from '../lib/commands';
  import { studio } from '../lib/state.svelte';
  import { toasts } from '../lib/toasts.svelte';

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

{#if signingOut}
  <Dialog
    title="Sign every browser out?"
    why="Every browser signed in to Studio or the dashboard on this machine is signed out, this one included. Nothing else changes."
    danger
    actions={[{ label: 'Sign out', kind: 'danger', cmd: command.signOut(), onpress: () => void out() }]}
    onclose={() => (signingOut = false)}
  />
{/if}
