<script lang="ts">
  // The centre of the Lens: the work in focus, drawn as light around its
  // words. Orbit is five stage rings with a body circling the one the work is
  // on; Gyroscope is three tilted rings for its stage, the hour and its spend.
  // The drawing is aria-hidden; a hidden line says the stage, and the core's
  // own text says the rest.
  import type { Snippet } from 'svelte';
  import { arc, css, glowOf, polar } from '../css';
  import { signalOf, STAGE_NAMES, type Stage } from '../signals';
  import { motion } from '../studio';
  import type { Orbiter } from '../types';

  interface Props {
    style?: 'orbit' | 'gyroscope';
    stage: Stage;
    /** Gyroscope: how far through the hour the work is, 0 to 1. */
    hour?: number;
    /** Gyroscope: spend against its cap, 0 to 1, or null when there is no cap. */
    spend?: number | null;
    names?: readonly string[];
    orbiters?: Orbiter[];
    orbitRadius?: number;
    tilt?: boolean;
    small?: boolean;
    core?: Snippet;
  }

  let {
    style = 'orbit',
    stage,
    hour = 0.3,
    spend = null,
    names = STAGE_NAMES,
    orbiters = [],
    orbitRadius = 330,
    tilt = true,
    small = false,
    core = undefined,
  }: Props = $props();

  const INK = ['var(--rest)', 'var(--shaping)', 'var(--moving)', 'var(--needs)', 'var(--good)'];
  const RINGS = [180, 196, 212, 228, 244];

  const current = $derived(Math.max(0, Math.min(4, stage | 0)));
  const ink = $derived(INK[current]);
  const flat = $derived(motion() === 'none');

  let inner = $state<HTMLDivElement | undefined>();
  let wrap = $state<HTMLDivElement | undefined>();

  function turn(event: MouseEvent) {
    if (motion() !== 'full' || !wrap || !inner || flat || !tilt) {
      return;
    }

    const box = wrap.getBoundingClientRect();
    const x = (event.clientX - box.left) / box.width - 0.5;
    const y = (event.clientY - box.top) / box.height - 0.5;

    inner.style.transform = `rotateX(${(-y * 18).toFixed(1)}deg) rotateY(${(x * 26).toFixed(1)}deg)`;
  }

  function rest() {
    if (inner) {
      inner.style.transform = '';
    }
  }

  function ringBox(size: number, rotation: string) {
    return { width: `${size}px`, height: `${size}px`, margin: `-${size / 2}px 0 0 -${size / 2}px`, transform: flat ? 'none' : rotation };
  }

  const hourPoint = $derived(polar(220, 214, (hour ?? 0) * 360));
  const spendFraction = $derived(spend === null || spend === undefined ? null : Math.max(0, Math.min(1, spend)));
  const spendPoint = $derived(spendFraction === null ? null : polar(190, 184, spendFraction * 360));
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div bind:this={wrap} class="ls-halo-wrap" class:small onmousemove={style === 'gyroscope' ? turn : undefined} onmouseleave={rest}>
  <p class="ls-sr">Stage {current + 1} of 5: {names[current]}.</p>

  {#if style === 'gyroscope'}
    <div class="gyro">
      <div class="gyro-in" bind:this={inner}>
        <div class="gring" use:css={ringBox(500, 'rotateX(66deg)')}>
          <svg viewBox="0 0 500 500" width="500" height="500" aria-hidden="true" class="gspin">
            <circle cx="250" cy="250" r="244" fill="none" stroke-width="10" use:css={{ stroke: 'color-mix(in srgb, var(--accent) 16%, transparent)' }} />
            {#each names as _, k (k)}
              {#if k <= current}
                <circle
                  cx="250"
                  cy="250"
                  r="244"
                  fill="none"
                  use:css={{ ...arc(244, -144 + k * 72 + 3, 66, INK[k], k === current ? 12 : 8, 250), opacity: k === current ? 1 : 0.6, filter: glowOf(INK[k], k === current ? 10 : 4) }}
                />
              {/if}
            {/each}
          </svg>
        </div>
        <div class="gring" use:css={ringBox(440, 'rotateY(64deg) rotateX(12deg)')}>
          <svg viewBox="0 0 440 440" width="440" height="440" aria-hidden="true" class="gspin" use:css={{ 'animation-duration': '36s', 'animation-direction': 'reverse' }}>
            <circle cx="220" cy="220" r="214" fill="none" stroke-width="2" stroke-dasharray="3 6" use:css={{ stroke: 'color-mix(in srgb, var(--accent) 30%, transparent)' }} />
            <circle cx="220" cy="220" r="214" fill="none" use:css={{ ...arc(214, 0, Math.max(1, (hour ?? 0) * 360), 'var(--accent)', 4, 220), filter: glowOf('var(--accent)', 6) }} />
            <circle cx={hourPoint.x.toFixed(1)} cy={hourPoint.y.toFixed(1)} r="7" use:css={{ fill: 'var(--ink-strong)', filter: glowOf('var(--accent)', 8) }} />
          </svg>
        </div>
        <div class="gring" use:css={ringBox(380, 'rotateX(-56deg) rotateY(28deg)')}>
          <svg viewBox="0 0 380 380" width="380" height="380" aria-hidden="true" class="gspin" use:css={{ 'animation-duration': '80s' }}>
            <circle cx="190" cy="190" r="184" fill="none" stroke-width="5" use:css={{ stroke: 'color-mix(in srgb, var(--gauge) 22%, transparent)' }} />
            {#if spendFraction !== null && spendPoint}
              <circle cx="190" cy="190" r="184" fill="none" use:css={{ ...arc(184, 0, Math.max(1, spendFraction * 360), 'var(--gauge)', 5, 190), filter: glowOf('var(--gauge)', 6) }} />
              <circle cx={spendPoint.x.toFixed(1)} cy={spendPoint.y.toFixed(1)} r="7" use:css={{ fill: 'var(--ink-strong)', filter: glowOf('var(--gauge)', 8) }} />
            {/if}
          </svg>
        </div>
        <div class="core">{@render core?.()}</div>
      </div>
    </div>
  {:else}
    <svg viewBox="0 0 540 540" aria-hidden="true">
      {#each RINGS as radius, k (k)}
        <circle
          cx="270"
          cy="270"
          r={radius}
          fill="none"
          class="trk"
          use:css={{
            stroke: k === current ? ink : `color-mix(in srgb, var(--accent) ${k <= current ? 38 : 14}%, transparent)`,
            'stroke-width': k === current ? 2.5 : 1,
            filter: k === current ? glowOf(ink, 6) : null,
          }}
        />
      {/each}
      {#each RINGS.slice(0, current) as radius, k (k)}
        {@const point = polar(270, radius, 200 + k * 30)}
        <circle cx={point.x.toFixed(1)} cy={point.y.toFixed(1)} r="5" use:css={{ fill: INK[k], filter: glowOf(INK[k], 5) }} />
      {/each}
      <g class="spin">
        <circle cx="270" cy="270" r={RINGS[current]} fill="none" use:css={{ ...arc(RINGS[current], -70, 70, ink, 3), opacity: 0.25 }} />
        <circle cx="270" cy="270" r={RINGS[current]} fill="none" use:css={{ ...arc(RINGS[current], -24, 24, ink, 4), opacity: 0.6, filter: glowOf(ink, 6) }} />
        <circle
          cx="270"
          cy="270"
          r="10"
          class="obody"
          use:css={{ fill: 'var(--ink-strong)', stroke: ink, 'stroke-width': 4, transform: `translateY(-${RINGS[current]}px)`, filter: glowOf(ink, 12) }}
        />
      </g>
    </svg>
    {#each names as name, k (k)}
      <span class="olabel" class:cur={k === current} class:lit={k < current} aria-hidden="true" use:css={{ left: '50%', top: `${((270 - RINGS[k]) / 5.4).toFixed(2)}%` }}>{name}</span>
    {/each}
    <div class="core">{@render core?.()}</div>
  {/if}

  {#if orbiters.length}
    <div class="orbiters">
      {#each orbiters as orbiter, k (orbiter.key)}
        {@const angle = -60 + (k * 360) / orbiters.length}
        <div use:css={{ transform: `rotate(${angle.toFixed(1)}deg) translateY(-${orbitRadius}px) rotate(${(-angle).toFixed(1)}deg)` }}>
          <button
            type="button"
            class="orbiter"
            aria-label={`Bring into the halo: ${orbiter.title}, ${orbiter.word}`}
            use:css={{ '--c': `var(--${signalOf(orbiter.word).family})` }}
            onclick={() => orbiter.onpick?.()}
          ><i aria-hidden="true"></i><span class="ot">{orbiter.title}</span><span class="ow">{orbiter.word}</span></button>
        </div>
      {/each}
    </div>
  {/if}
</div>
