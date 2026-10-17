<script lang="ts">
  // One of a few options that each need a sentence, as rings that are radios;
  // the command beneath follows the pick.
  import { roving } from '../roving';

  interface Props {
    options: { name: string; desc?: string }[];
    selected?: number;
    label?: string;
    command?: (name: string) => string;
    onchange?: (name: string, index: number) => void;
  }

  let { options, selected = $bindable(0), label = undefined, command = undefined, onchange = undefined }: Props = $props();

  const buttons = $state<(HTMLButtonElement | undefined)[]>([]);

  function select(index: number) {
    selected = index;
    onchange?.(options[index].name, index);
  }
</script>

<div>
  <div class="ls-choices" role="radiogroup" aria-label={label}>
    {#each options as option, index (option.name)}
      <button
        bind:this={buttons[index]}
        type="button"
        role="radio"
        class="ls-choice"
        aria-checked={index === selected ? 'true' : 'false'}
        tabindex={index === selected ? 0 : -1}
        onclick={() => select(index)}
        onkeydown={(event) => roving(event, index, buttons, select)}
      ><span class="ring" aria-hidden="true"></span><b>{option.name}</b><span>{option.desc ?? ''}</span></button>
    {/each}
  </div>
  {#if command && options[selected]}<div class="ls-below"><code class="ls-cmd">{command(options[selected].name)}</code></div>{/if}
</div>
