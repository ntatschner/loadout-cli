<script lang="ts">
  // An amount against a threshold you set: a beam, a mark where the threshold
  // is, and past it the fill turning to Needs with the words saying so.
  // Thresholds stop nothing, so nothing here calls one a limit.
  interface Props {
    value: number;
    max: number;
    threshold?: number;
    label?: string;
    unit?: string;
    thresholdLabel?: string;
    overWords?: string;
    source?: string;
    format?: (value: number) => string;
    of?: (value: number) => string;
  }

  let {
    value,
    max,
    threshold = undefined,
    label = 'Meter',
    unit = undefined,
    thresholdLabel = 'threshold',
    overWords = 'past your threshold',
    source = undefined,
    format = String,
    of = undefined,
  }: Props = $props();

  const top = $derived(max || 100);
  const percent = $derived(Math.max(0, Math.min(100, (value / top) * 100)));
  const over = $derived(threshold !== undefined && value >= threshold);
  const figure = $derived(format(value) + (unit ? ` ${unit}` : ''));
  const at = $derived(threshold === undefined ? null : `${((threshold / top) * 100).toFixed(1)}%`);
</script>

<div class="ls-meter" class:over>
  <div class="top"><b>{figure}</b><span>{of ? of(value) : ''}</span><em>{over ? overWords : ''}</em></div>
  <div
    class="track"
    role="meter"
    aria-label={label}
    aria-valuemin={0}
    aria-valuemax={top}
    aria-valuenow={value}
    aria-valuetext={figure + (over ? `, ${overWords}` : '')}
  >
    <i class="fill" style:width={`${percent.toFixed(1)}%`}></i>
    {#if at}<u class="tick" style:left={at}></u><span class="tl" style:left={at}>{thresholdLabel}</span>{/if}
  </div>
  {#if source}<span class="src">{source}</span>{/if}
</div>
