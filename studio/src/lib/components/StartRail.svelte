<script lang="ts">
  // Where a session starts: an accent tab asking what it should do, a wide
  // input, Deploy, and the command that will run, updated as you type. An
  // empty task does nothing.
  import HaloButton from './HaloButton.svelte';

  interface Props {
    label?: string;
    value?: string;
    placeholder?: string;
    action?: string;
    id?: string;
    command?: (task: string) => string;
    ondeploy?: (task: string) => void;
    disabledReason?: string;
  }

  let {
    label = 'What should it do?',
    value = $bindable(''),
    placeholder = 'Describe the task in a sentence',
    action = 'Deploy',
    id = `ls-start-${Math.random().toString(36).slice(2, 7)}`,
    command = (task: string) => `$ loadout --task "${task}"`,
    ondeploy = undefined,
    disabledReason = undefined,
  }: Props = $props();

  function deploy() {
    const task = value.trim();

    if (task && !disabledReason) {
      ondeploy?.(task);
    }
  }
</script>

<div class="ls-start">
  <label for={id} class="tab">{label}</label>
  <div class="field">
    <input {id} bind:value {placeholder} autocomplete="off" onkeydown={(event) => event.key === 'Enter' && deploy()} />
    <HaloButton label={action} onpress={deploy} inactive={!!disabledReason} />
  </div>
  <code class="ls-cmd">{disabledReason ?? command(value)}</code>
</div>
