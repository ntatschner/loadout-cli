<script lang="ts">
  // Studio's button: a word in spaced capitals beside a ring of light, with
  // the command it runs written underneath. A disabled one says why in place
  // of the command, rather than greying out silently.
  interface Props {
    label: string;
    /** "" (accent), "needs" (answers to a question), "danger" (acknowledging a problem). */
    kind?: '' | 'needs' | 'danger';
    size?: '' | 'small' | 'big';
    /** The filled ring: the strong answer in a set. */
    solid?: boolean;
    /** One character drawn in the ring. */
    glyph?: string;
    /** The command the press runs, shown under it. */
    cmd?: string;
    /** When set, the button does nothing and this is shown instead of a command. */
    disabledReason?: string;
    describedBy?: string;
    /** Does nothing and says so to assistive technology, where the reason is already written beside it. */
    inactive?: boolean;
    onpress?: (event: MouseEvent) => void;
    /** For keeping focus on this button across a redraw. */
    key?: string;
    /** Where the Ideas page's Next button finds this one. */
    next?: string;
  }

  let {
    label,
    kind = '',
    size = '',
    solid = false,
    glyph = undefined,
    cmd = undefined,
    disabledReason = undefined,
    describedBy = undefined,
    inactive = false,
    onpress = undefined,
    key = undefined,
    next = undefined,
  }: Props = $props();

  const classes = $derived(['ls-halo', kind, size, solid ? 'solid' : ''].filter(Boolean).join(' '));

  function press(event: MouseEvent) {
    if (!disabledReason && !inactive && onpress) {
      onpress(event);
    }
  }
</script>

{#snippet button()}
  <button
    type="button"
    class={classes}
    aria-disabled={disabledReason || inactive ? 'true' : undefined}
    aria-describedby={describedBy}
    data-k={key}
    data-next={next}
    onclick={press}
  ><span class="ring" aria-hidden="true">{#if glyph}<b>{glyph}</b>{/if}</span>{label}</button>
{/snippet}

{#if cmd || disabledReason}
  <div class="ls-action">
    {@render button()}
    <code class="ls-cmd">{disabledReason ?? cmd}</code>
  </div>
{:else}
  {@render button()}
{/if}
