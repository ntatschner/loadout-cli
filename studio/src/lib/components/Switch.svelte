<script lang="ts">
  // One setting on or off, with On or Off written beside the light and, under
  // it, what it does and where the choice is kept.
  interface Props {
    label: string;
    sub?: string;
    checked?: boolean;
    /** The command the change runs, when it changes Loadout's config rather than this browser. */
    cmd?: string;
    disabledReason?: string;
    onchange?: (on: boolean) => void;
  }

  let { label, sub = undefined, checked = $bindable(false), cmd = undefined, disabledReason = undefined, onchange = undefined }: Props = $props();

  function toggle() {
    if (disabledReason) {
      return;
    }

    checked = !checked;
    onchange?.(checked);
  }
</script>

<div class="ls-action ls-switch-row">
  <button type="button" role="switch" class="ls-switch" aria-checked={checked ? 'true' : 'false'} aria-disabled={disabledReason ? 'true' : undefined} onclick={toggle}>
    <span class="lbl">{label}</span>
    {#if sub}<span class="sub">{sub}</span>{/if}
    <span class="track" aria-hidden="true"><span class="pill"><span class="knob"></span></span><span class="state">{checked ? 'On' : 'Off'}</span></span>
  </button>
  {#if disabledReason}<code class="ls-cmd">{disabledReason}</code>{:else if cmd}<code class="ls-cmd">{cmd}</code>{/if}
</div>
