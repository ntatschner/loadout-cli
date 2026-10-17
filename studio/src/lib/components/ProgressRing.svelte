<script lang="ts">
  // A ring that fills for stages, the Meter's round form.
  let { value }: { value: number } = $props();

  const circumference = 2 * Math.PI * 48;
  const fraction = $derived(Math.max(0, Math.min(1, value)));
</script>

<div class="ls-progress">
  <svg viewBox="0 0 120 120" aria-hidden="true">
    <circle class="bg" cx="60" cy="60" r="48" />
    <circle class="fg" cx="60" cy="60" r="48" transform="rotate(-90 60 60)" style:stroke-dasharray={`${(circumference * fraction).toFixed(1)} ${circumference.toFixed(1)}`} />
  </svg>
  <span class="pct" aria-hidden="true">{Math.round(fraction * 100)}%</span>
</div>
