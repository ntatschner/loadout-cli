<script lang="ts">
  // The pages Studio does not draw yet, each with the command that shows the
  // same thing in a terminal. Ideas and Teams are also on the classic
  // dashboard, which Studio links to rather than copies.
  import Command from '../lib/components/Command.svelte';

  let { page }: { page: string } = $props();

  const PAGES: Record<string, [string, string, string, boolean]> = {
    ideas: ['Ideas', '$ loadout idea list', 'Ideas you are shaping, a round at a time.', true],
    teams: ['Teams', '$ loadout team runs', 'Teams and their runs, with the office.', true],
    context: ['Context', '$ loadout instructions explain', 'What a session reads before it starts, and what it costs in tokens.', false],
    spend: ['Spend', '$ loadout usage', 'Tokens used, by day, project and model.', false],
    projects: ['Projects', '$ loadout project list', 'The projects Loadout knows on this machine.', false],
  };

  const shown = $derived(PAGES[page] ?? ['Not found', '$ loadout --help', "There's no page here.", false]);
</script>

<div class="head"><h1>{shown[0]}</h1></div>
<div class="placeholder">
  <p class="calm">{shown[2]} Studio doesn't draw this page yet.{#if shown[3]} The classic dashboard does: <a href="/">open it</a>.{/if}</p>
  <Command why="Until then, in a terminal:" command={shown[1]} />
</div>
