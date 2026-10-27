<script lang="ts">
  // Theme, accent, density, motion, the background and the Lens halo. Kept in Loadout's
  // config, so every browser on this machine shows Studio the same way; no
  // theme set follows the system, and the system's reduced motion always wins.
  import { command } from '../lib/commands';
  import { studio } from '../lib/state.svelte';

  type Option = [string, string];

  const groups: { key: string; legend: string; options: Option[]; value: () => string; note?: string }[] = [
    { key: 'studio-theme', legend: 'Theme', options: [['', 'Follow the system'], ['dark', 'Dark'], ['light', 'Light'], ['hc-dark', 'High contrast dark'], ['hc-light', 'High contrast light']], value: () => studio.prefs?.theme ?? '' },
    { key: 'studio-accent', legend: 'Accent', options: [['cyan', 'Cyan'], ['ice', 'Ice'], ['magenta', 'Magenta'], ['violet', 'Violet']], value: () => studio.prefs?.accent ?? 'cyan' },
    { key: 'studio-density', legend: 'Density', options: [['compact', 'Compact'], ['comfortable', 'Comfortable'], ['roomy', 'Roomy']], value: () => studio.prefs?.density ?? 'comfortable' },
    { key: 'studio-motion', legend: 'Motion', options: [['full', 'Full'], ['reduced', 'Reduced'], ['none', 'None']], value: () => studio.prefs?.motion ?? 'full' },
    {
      key: 'studio-backdrop',
      legend: 'Background',
      options: [['plain', 'Plain'], ['map', 'Field map, still'], ['map-animated', 'Field map, animated']],
      value: () => studio.prefs?.backdrop ?? 'plain',
      note: 'The animated map moves only under full motion and pauses while anything waits for you. Pure Focus, Ambient and high contrast keep the plain ground.',
    },
    { key: 'studio-halo', legend: 'Lens halo', options: [['orbit', 'Orbit'], ['gyroscope', 'Gyroscope']], value: () => studio.prefs?.halo ?? 'orbit' },
  ];

  const reduced = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
</script>

<div class="appear">
  {#each groups as group (group.key)}
    <fieldset class="seg" disabled={studio.watchOnly}>
      <legend>{group.legend}</legend>
      <div class="opts">
        {#each group.options as [value, label] (value)}
          <label>
            <input type="radio" name={`ap-${group.key}`} {value} checked={group.value() === value} onchange={() => void studio.setPreference(group.key, value)} />
            {label}
          </label>
        {/each}
      </div>
      <code class="ls-cmd">{studio.watchOnly ? `Studio is only watching · ${command.setting(group.key, group.value())}` : command.setting(group.key, group.value())}</code>
      {#if group.note}
        <p class="calm small">{group.note}</p>
      {/if}
    </fieldset>
  {/each}
  {#if reduced}
    <p class="calm small">This system asks for reduced motion, so nothing loops or travels whatever is chosen here.</p>
  {/if}
</div>
