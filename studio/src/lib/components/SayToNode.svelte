<script lang="ts">
  // Typing at a live node, behind a second credential: the passphrase set in
  // a terminal, swapped for a 30-minute grant held only by this page. Or,
  // with lead, a message to the lead, which needs only Studio's sign-in and
  // is read at the start of its next round.
  import Command from './Command.svelte';
  import HaloButton from './HaloButton.svelte';
  import { quoted } from '../commands';

  interface Props {
    run: string;
    node?: string | null;
    lead?: boolean;
    disabledReason?: string;
    /** unset: no passphrase on this machine; locked: one is set; open: this page holds a grant; ended: the node is not running. */
    access?: 'unset' | 'locked' | 'open' | 'ended';
    minutesLeft?: number;
    /** Swaps the passphrase for a grant; resolves whether it was right. */
    onunlock?: (passphrase: string) => Promise<boolean> | boolean;
    /** Sends it; resolves whether it was taken. */
    onsend?: (message: string) => Promise<boolean | void> | boolean | void;
  }

  let { run, node = null, lead = false, disabledReason = undefined, access = 'locked', minutesLeft = 30, onunlock = undefined, onsend = undefined }: Props = $props();

  let message = $state('');
  let passphrase = $state('');
  let said = $state('');
  let refused = $state(false);
  let unlocked = $state(false);
  let wrong = $state('');
  let box = $state<HTMLTextAreaElement | undefined>();

  const shown = $derived(unlocked && access === 'locked' ? 'open' : access);
  const who = $derived(node ?? 'this node');
  const line = $derived(
    lead
      ? `$ loadout team message ${run} --message ${quoted(message.trim() || '…')}`
      : `$ loadout team say ${run} --node ${node} --message ${quoted(message.trim() || '…')}`,
  );

  async function unlock() {
    const ok = onunlock ? await onunlock(passphrase) : passphrase.length > 0;

    if (ok) {
      unlocked = true;
      wrong = '';
      passphrase = '';
      queueMicrotask(() => box?.focus());
    } else {
      wrong = '✕ That is not the passphrase.';
    }
  }

  async function send() {
    const text = message.trim();

    if (!text) {
      refused = true;
      said = '✕ Write something to send first.';

      return;
    }

    const taken = await onsend?.(text);

    if (taken === false) {
      refused = true;
      said = '✕ It was not sent.';

      return;
    }

    refused = false;
    said = lead ? '✓ Sent. The lead reads it at the start of its next round, once.' : `✓ Sent. It reaches ${node} the next time it says anything, once.`;
    message = '';
  }
</script>

<section class="ls-say" aria-label={lead ? 'Say something to the lead' : `Say something to ${who}`}>
  {#if lead}
    <h4>Say something to the lead</h4>
  {:else}
    <h4>Say something to {who} now</h4>
  {/if}

  {#if !lead && shown === 'ended'}
    <p>{node ?? 'This node'} is not running, so there is nothing to type at. Say something to the lead instead: it reads it at the start of its next round.</p>
  {:else if !lead && shown === 'unset'}
    <p>Nothing on this machine can type at a node until a passphrase is set for it. It is set in a terminal, so this page cannot set the passphrase that guards it.</p>
    <Command why="In a terminal:" command={'$ loadout team attach set --passphrase "<something worth having>"'} />
  {:else if !lead && shown === 'locked'}
    <p>This puts words into a process running with your file access, so it needs the passphrase set for it as well as your sign-in. It is swapped for a grant that lasts 30 minutes and lives only in this page.</p>
    <div class="row">
      <div class="ls-field">
        <label class="lab" for={`say-pass-${node}`}>Passphrase</label>
        <input id={`say-pass-${node}`} name="attach-passphrase" class="ls-text" type="password" autocomplete="off" bind:value={passphrase} />
      </div>
      <HaloButton label="Unlock for 30 minutes" size="small" cmd="asks the daemon for a 30-minute grant · stores nothing" {disabledReason} onpress={() => void unlock()} />
    </div>
    <p class="ls-said no" role="alert">{wrong}</p>
  {:else}
    <div class="row">
      <textarea bind:this={box} class="ls-text" rows="2" name={lead ? 'lead-message' : 'node-message'} placeholder={lead ? 'leave the tests alone' : 'stop, wrong file'} aria-label={lead ? 'Message to the lead' : `Message to ${node}`} spellcheck="false" bind:value={message}></textarea>
      <div class="ls-action">
        <HaloButton label="Send" size="small" {disabledReason} onpress={() => void send()} />
        <code class="ls-cmd">{line}</code>
      </div>
    </div>
    <p class={refused ? 'ls-said no' : 'ls-said'} role="status">{said}</p>
    {#if lead}
      <p class="grant">It steers the run without interrupting a node mid-thought. To stop a node now, open its stream under What it said.</p>
    {:else}
      <span class="grant">Unlocked · the grant ends in {minutesLeft} min</span>
    {/if}
  {/if}
</section>
