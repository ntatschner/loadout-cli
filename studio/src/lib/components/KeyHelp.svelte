<script lang="ts">
  // Every key in Pure Focus, in a dialog: ? opens it.
  import Dialog from './Dialog.svelte';
  import HaloButton from './HaloButton.svelte';
  import { PURE_KEYS } from '../pure';

  let { onclose = undefined }: { onclose?: () => void } = $props();
</script>

<Dialog title="Keys in Pure Focus" why="Single keys work whenever you are not typing. In a terminal every key goes to the agent; Ctrl+] leaves it." cancelLabel="Close" actions={[]} {onclose}>
  <div class="ls-keyhelp">
    {#each PURE_KEYS as [group, rows] (group)}
      <section>
        <h3>{group}</h3>
        <dl>
          {#each rows as [keys, does] (does)}
            <dt>{#each keys as one (one)}<kbd class="ls-kbd">{one}</kbd>{/each}</dt>
            <dd>{does}</dd>
          {/each}
        </dl>
      </section>
    {/each}
  </div>
  <!-- Given no actions the dialog draws no button, and something in it must
       take the keyboard for Escape to reach it. -->
  <div class="acts"><HaloButton label="Close" size="small" cmd="Esc" onpress={() => onclose?.()} /></div>
</Dialog>
