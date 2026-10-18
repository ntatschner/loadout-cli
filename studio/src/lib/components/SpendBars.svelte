<script lang="ts">
  // Tokens by project, model or agent as bars, the twenty largest, with a
  // folder that is not a registered project hatched and saying so. By day,
  // columns oldest first, read by hovering or with the arrow keys, Home and
  // End, with a readout a screen reader hears.
  import { css } from '../css';
  import { tokens } from '../glyphs';

  interface Props {
    groups: { name: string; value: number; registered?: boolean }[];
    by: string;
    limit?: number;
    label?: string;
    unit?: string;
  }

  let { groups, by, limit = 20, label = undefined, unit = 'tokens' }: Props = $props();

  const rows = $derived([...groups].sort((a, b) => b.value - a.value));
  const shown = $derived(rows.slice(0, limit));
  const top = $derived(shown[0]?.value || 1);
  const most = $derived(Math.max(1, ...groups.map((one) => one.value)));
  const step = $derived(Math.max(1, Math.ceil(groups.length / 6)));

  let picked = $state<number | null>(null);
  const current = $derived(picked === null ? groups.length - 1 : Math.max(0, Math.min(groups.length - 1, picked)));

  function keys(event: KeyboardEvent) {
    const to = event.key === 'ArrowLeft' ? current - 1 : event.key === 'ArrowRight' ? current + 1 : event.key === 'Home' ? 0 : event.key === 'End' ? groups.length - 1 : null;

    if (to !== null) {
      event.preventDefault();
      picked = Math.max(0, Math.min(groups.length - 1, to));
    }
  }
</script>

<div class="ls-bars">
  {#if by === 'day'}
    <p class="readout" aria-live="polite">{#if groups[current]}<span><b>{groups[current].name}</b> · {tokens(groups[current].value)} {unit}</span>{/if}</p>
    <!-- svelte-ignore a11y_no_noninteractive_tabindex, a11y_no_noninteractive_element_interactions -->
    <div class="cols" tabindex="0" role="group" aria-roledescription="column chart" aria-label={`${label ?? 'Tokens by day'}. Left and right arrows read each day.`} onkeydown={keys}>
      {#each groups as day, index (day.name)}
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <div class={['c', index === current ? 'cur' : ''].filter(Boolean).join(' ')} onmouseenter={() => (picked = index)}><span use:css={{ height: `${Math.max(1, (day.value / most) * 100).toFixed(2)}%` }}></span></div>
      {/each}
    </div>
    <div class="axis" aria-hidden="true">{#each groups as day, index (day.name)}<span>{index % step === 0 ? day.name : ''}</span>{/each}</div>
    <p class="keys">Hover a day, or focus the chart and use the arrow keys.</p>
  {:else}
    <ol class="rows" aria-label={label ?? `Tokens by ${by}`}>
      {#each shown as row (row.name)}
        <li class={row.registered === false ? 'away' : ''}>
          <span class="nm" title={row.name}><span>{row.name}</span>{#if row.registered === false}<span class="ls-tag">not a project</span>{/if}</span>
          <span class="track" aria-hidden="true"><span class="fill" use:css={{ width: `${Math.max(0.5, (row.value / top) * 100).toFixed(2)}%` }}></span></span>
          <span class="v">{tokens(row.value)}</span>
        </li>
      {/each}
    </ol>
    {#if rows.length > limit}<p class="left">The {rows.length - limit} smallest are left out here.</p>{/if}
  {/if}
</div>
