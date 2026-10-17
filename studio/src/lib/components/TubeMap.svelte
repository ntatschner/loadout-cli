<script lang="ts">
  // The Map: every piece of work is a line in its identity colour, stations
  // are stages, and Needs you is the interchange every line passes through.
  // The drawing is aria-hidden; each line's board is a button that says what
  // the drawing shows.
  import Signal from './Signal.svelte';
  import { css, glowOf } from '../css';
  import { STAGE_NAMES, wordFor } from '../signals';
  import { fullMotion } from '../studio';
  import { EVENT_GLYPH, LANE, STATION_X, TOP_Y, X1, lengthTo, lineColour, pathOf, route, stageOf, yAt, type TubeLine } from '../tube';

  interface Props {
    lines: TubeLine[];
    picked?: string;
    onpick?: (id: string) => void;
    stations?: readonly string[];
    label?: string;
    idleAfter?: number;
  }

  let { lines, picked = $bindable(undefined), onpick = undefined, stations = STAGE_NAMES, label = 'Map', idleAfter = 10 }: Props = $props();

  let wrap = $state<HTMLDivElement | undefined>();

  const lanes = $derived.by(() => {
    let y = TOP_Y;

    return lines.map((line) => {
      const height = line.workers ? LANE + 66 : LANE;
      const cy = y + 30;
      y += height;

      const points = route(String(line.code), cy);
      const stage = stageOf(line);
      const tx = STATION_X[stage];

      return {
        line,
        cy,
        points,
        stage,
        d: pathOf(points),
        colour: lineColour(line.code),
        total: lengthTo(points, X1),
        reached: lengthTo(points, tx),
        tx,
        word: wordFor(line, idleAfter),
      };
    });
  });

  const height = $derived(TOP_Y + lines.reduce((sum, line) => sum + (line.workers ? LANE + 66 : LANE), 0) + 10);
  const moving = fullMotion();

  function pick(id: string) {
    picked = id;
    onpick?.(id);
    queueMicrotask(() => wrap?.querySelector<HTMLElement>('.board[aria-pressed="true"]')?.focus());
  }
</script>

<div bind:this={wrap} class="ls-tube" role="group" aria-label={label}>
  <div class="box" use:css={{ height: `${height}px` }}>
    <svg viewBox={`0 0 1280 ${height}`} width="1280" height={height} aria-hidden="true">
      {#each STATION_X as sx, k (k)}
        <rect x={sx - 90} y="34" width="180" height={height - 34} use:css={{ fill: k === 3 ? 'var(--needs-wash)' : `color-mix(in srgb, var(--accent) ${k % 2 ? 4.5 : 2}%, transparent)` }} />
      {/each}
      {#if lanes.length}
        <rect x={STATION_X[3] - 11} y={lanes[0].cy - 22} width="22" height={lanes[lanes.length - 1].cy - lanes[0].cy + 44} rx="11" fill="none" stroke-width="2" use:css={{ stroke: 'var(--ink)', filter: glowOf('var(--ink)', 6) }} />
      {/if}
      {#each lanes as lane (lane.line.id)}
        {@const isPicked = lane.line.id === picked}
        <path d={lane.d} fill="none" stroke-width="8" stroke-linejoin="round" stroke-linecap="round" use:css={{ stroke: lane.colour, opacity: 0.55, 'stroke-dasharray': '14 9' }} />
        <path d={lane.d} fill="none" stroke-width="8" stroke-linejoin="round" stroke-linecap="round" class="trk" use:css={{ stroke: lane.colour, 'stroke-dasharray': `${lane.reached.toFixed(1)} ${(lane.total + 20).toFixed(1)}`, filter: glowOf(lane.colour, isPicked ? 9 : 5) }} />
        <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
        <path d={lane.d} fill="none" stroke="transparent" stroke-width="26" class="hit" onclick={() => pick(lane.line.id)} />
        {#each STATION_X as sx, k (k)}
          {@const yy = yAt(lane.points, sx)}
          {#if k === 3}
            <circle cx={sx} cy={yy.toFixed(1)} r="8" stroke-width="3" use:css={{ fill: 'var(--ground)', stroke: 'var(--ink-strong)' }} />
          {:else}
            <line x1={sx} y1={(yy - 9).toFixed(1)} x2={sx} y2={(k === 4 ? yy + 9 : yy).toFixed(1)} stroke-width="4" stroke-linecap="round" use:css={{ stroke: k <= lane.stage ? 'var(--ink-strong)' : lane.colour }} />
          {/if}
        {/each}
        {#each lane.line.workers ?? [] as worker, j (j)}
          {@const by = lane.cy + 24 + j * 22}
          {@const ex = STATION_X[2] + 14 + j * 8 + 118}
          <path d={`M${STATION_X[2]} ${lane.cy} L${STATION_X[2] + (by - lane.cy)} ${by} L${ex} ${by}`} fill="none" stroke-width="5" stroke-linejoin="round" stroke-linecap="round" use:css={{ stroke: lane.colour, opacity: 0.8 }} />
          <circle cx={ex} cy={by} r="6" stroke-width="2" use:css={{ fill: /waiting|reported/.test(worker[1]) ? 'var(--ground)' : 'var(--ink-strong)', stroke: lane.colour }} />
        {/each}
        {#if lane.line.blocked}
          {@const sy = yAt(lane.points, lane.tx)}
          {@const sx2 = lane.tx + 22}
          <line x1={sx2} y1={sy} x2={sx2} y2={sy - 30} stroke-width="2" use:css={{ stroke: 'var(--ink)' }} />
          <circle cx={sx2} cy={sy - 30} r="10" stroke-width="2" use:css={{ fill: 'var(--bad)', stroke: 'var(--ink-strong)', filter: glowOf('var(--bad)', 8) }} />
          <rect x={sx2 - 6} y={sy - 32} width="12" height="4" use:css={{ fill: 'var(--ink-strong)' }} />
        {/if}
      {/each}
    </svg>

    {#each stations as name, k (k)}
      <span class="stn" class:wait={k === 3} aria-hidden="true" use:css={{ left: `${STATION_X[k]}px` }}>{name}</span>
    {/each}

    {#each lanes as lane (lane.line.id)}
      {@const line = lane.line}
      {#each line.checks ?? [] as check, j (j)}
        {@const cx = STATION_X[3] + 45 + j * 45}
        {@const met = check[1] === 'met'}
        {@const unmet = check[1] === 'unmet'}
        <span
          class="mk"
          aria-hidden="true"
          use:css={{
            left: `${cx}px`,
            top: `${yAt(lane.points, cx).toFixed(1)}px`,
            background: met ? 'var(--good)' : 'var(--ground)',
            'border-color': met ? 'var(--ground)' : unmet ? 'var(--bad)' : 'var(--muted)',
            color: met ? 'var(--on-signal)' : unmet ? 'var(--bad)' : 'var(--muted)',
          }}
        >{met ? '✓' : unmet ? '✕' : '○'}</span>
      {/each}
      {#each line.workers ?? [] as worker, j (j)}
        <span class="wlabel" aria-hidden="true" use:css={{ left: `${STATION_X[2] + 14 + j * 8 + 118 + 12}px`, top: `${lane.cy + 24 + j * 22}px` }}>{worker[0]} · {worker[1]}</span>
      {/each}
      {#each (line.events ?? []).slice(0, 5) as event, j (j)}
        {@const same = (line.events ?? []).slice(0, j).filter((other) => other.stage === event.stage).length}
        {@const ex = STATION_X[event.stage] - 26 - same * 22}
        <span
          class="mk"
          aria-hidden="true"
          use:css={{
            left: `${ex.toFixed(1)}px`,
            top: `${(yAt(lane.points, ex) - 22).toFixed(1)}px`,
            'border-color': event.kind === 'failed' || event.kind === 'blocked' ? 'var(--bad)' : event.kind === 'asked' ? 'var(--needs)' : lane.colour,
            opacity: (1 - j * 0.15).toFixed(2),
          }}
        >{EVENT_GLYPH[event.kind] ?? '·'}</span>
      {/each}
      <span
        class="train"
        class:wait={line.waiting}
        class:anim-ok={moving}
        aria-hidden="true"
        use:css={{
          '--tc': `color-mix(in srgb, ${lane.colour} calc(var(--glow-on) * 100%), transparent)`,
          background: lane.colour,
          'offset-path': `path('${lane.d}')`,
          'offset-distance': `${lane.reached.toFixed(1)}px`,
        }}
      >{line.code}</span>
      {@const facts = line.blocked ?? line.facts ?? ''}
      <button
        type="button"
        class="board"
        aria-pressed={line.id === picked ? 'true' : 'false'}
        aria-label={`Line ${line.code}, ${line.kind ?? ''}: ${line.title}, ${lane.word}${facts ? `. ${facts}` : ''}`}
        title={line.title}
        use:css={{ top: `${lane.cy - 24}px` }}
        onclick={() => pick(line.id)}
      ><span class="ls-badge" use:css={{ '--lc': lane.colour }}>{line.code}</span><span class="bt"><span class="t">{line.title}</span><Signal word={lane.word} /> <span class="x">{facts}</span></span></button>
    {/each}
  </div>
</div>
