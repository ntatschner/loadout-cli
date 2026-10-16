<script lang="ts">
  // What a browser the daemon does not know sees: how to open Studio signed
  // in, and nothing else. There is no box to paste a token into; the
  // terminal command signs the browser in by itself.
  import HaloButton from '../lib/components/HaloButton.svelte';
  import { command } from '../lib/commands';

  let copied = $state(false);
  const line = command.open().replace(/^\$ /, '');

  async function copy() {
    try {
      await navigator.clipboard.writeText(line);
      copied = true;
    } catch {
      copied = false;
    }
  }
</script>

<main id="main" class="page signedout" tabindex="-1">
  <div class="mark"><i aria-hidden="true"></i><b>LOADOUT</b> STUDIO</div>
  <h1>This browser isn't signed in to Studio</h1>
  <p class="calm">Studio only answers browsers it has signed in. In a terminal on this machine, run:</p>
  <div class="ls-action">
    <code class="ls-cmdline">{command.open()}</code>
    <HaloButton label={copied ? 'Copied' : 'Copy'} size="small" onpress={() => void copy()} />
  </div>
  <p class="calm small">It opens Studio in your browser, signed in. Signing in lasts 90 days, or until somebody signs every browser out.</p>
</main>
