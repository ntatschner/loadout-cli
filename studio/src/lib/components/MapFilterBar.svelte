<script lang="ts">
  // The filters over Home's Map and Lens: the presets, then a chip per kind
  // and per state, then the projects to hide. Every change goes straight to
  // config through onchange; the bar keeps nothing of its own, so it reads
  // what loadout config set it to as well as what it set.
  import {
    KINDS,
    KIND_NAMES,
    PRESETS,
    PRESET_NAMES,
    STATES,
    STATE_NAMES,
    presetOf,
    toggled,
    type MapFilter,
    type PresetName,
  } from '../mapFilter';

  let { filter, projects, onchange }: { filter: MapFilter; projects: string[]; onchange: (next: MapFilter) => void } = $props();

  const preset = $derived(presetOf(filter));
  const hiddenProjects = $derived(filter.projects.filter((project) => projects.includes(project)));

  function hide(project: string) {
    onchange({
      ...filter,
      projects: filter.projects.includes(project) ? filter.projects.filter((one) => one !== project) : [...filter.projects, project],
    });
  }
</script>

<div class="mapfilters" role="region" aria-label="What Home shows">
  <div class="views" role="group" aria-label="Presets">
    {#each Object.keys(PRESETS) as name (name)}
      <button type="button" aria-pressed={preset === name ? 'true' : 'false'} onclick={() => onchange(PRESETS[name as PresetName])}>{PRESET_NAMES[name as PresetName]}</button>
    {/each}
  </div>
  {#if !preset}<span class="custom">Custom</span>{/if}

  <div class="chips" role="group" aria-label="Kind">
    {#each KINDS as kind (kind)}
      {@const on = filter.kinds.includes(kind)}
      <button
        type="button"
        class="chip"
        aria-pressed={on ? 'true' : 'false'}
        aria-disabled={on && filter.kinds.length === 1 ? 'true' : undefined}
        title={on && filter.kinds.length === 1 ? 'One kind stays on' : undefined}
        onclick={() => onchange({ ...filter, kinds: toggled(filter.kinds, kind, KINDS) })}
      >{KIND_NAMES[kind]}</button>
    {/each}
  </div>

  <div class="chips" role="group" aria-label="State">
    {#each STATES as state (state)}
      {@const on = filter.states.includes(state)}
      <button
        type="button"
        class="chip"
        aria-pressed={on ? 'true' : 'false'}
        aria-disabled={on && filter.states.length === 1 ? 'true' : undefined}
        title={on && filter.states.length === 1 ? 'One state stays on' : undefined}
        onclick={() => onchange({ ...filter, states: toggled(filter.states, state, STATES) })}
      >{STATE_NAMES[state]}</button>
    {/each}
  </div>

  {#if projects.length}
    <details class="projects">
      <summary>Projects · {hiddenProjects.length ? `${hiddenProjects.join(', ')} hidden` : 'all shown'}</summary>
      <div class="chips" role="group" aria-label="Projects shown">
        {#each projects as project (project)}
          <button type="button" class="chip" aria-pressed={filter.projects.includes(project) ? 'false' : 'true'} onclick={() => hide(project)}>{project}</button>
        {/each}
      </div>
    </details>
  {/if}
</div>

<style>
  .mapfilters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 16px; margin-bottom: 16px; }
  .custom { font-size: 12.5px; letter-spacing: .24em; text-transform: uppercase; color: var(--ink-soft); }
  .chips { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; }
  .chip { min-height: 32px; padding: 0 10px; border: 0; border-radius: var(--radius-chip); background: none; box-shadow: inset 0 0 0 1px var(--control-edge); color: var(--muted); font: inherit; font-size: 12.5px; cursor: pointer; text-decoration: line-through; }
  .chip[aria-pressed="true"] { background: var(--tint-picked-c); color: var(--ink-strong); text-decoration: none; }
  .chip[aria-disabled="true"] { cursor: default; }
  .chip:focus-visible, summary:focus-visible { outline: 2px solid var(--focus); outline-offset: 2px; }
  .projects summary { min-height: 32px; display: inline-flex; align-items: center; cursor: pointer; font-size: 12.5px; color: var(--ink); }
  .projects[open] .chips { margin-top: 8px; }
</style>
