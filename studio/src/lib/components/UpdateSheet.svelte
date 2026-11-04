<script lang="ts">
  // Asking before an install or a roll back: whether to wait for the
  // daemon's runs or restart now, and what restarting now ends. Settings and
  // About both ask with this, so the two cannot word it differently or run
  // different commands; each runs the words it is handed its own way.
  import Dialog from './Dialog.svelte';
  import type { UpdateState } from '../api';
  import { lineFor } from '../commandLine';

  interface Props {
    updating: 'install' | 'roll-back';
    upd: UpdateState;
    /** The words for 'loadout update' the person chose. */
    onchoose: (words: string[]) => void;
    onclose: () => void;
  }

  let { updating, upd, onchoose, onclose }: Props = $props();

  const plural = (count: number, one: string) => `${count} ${one}${count === 1 ? '' : 's'}`;
  /** What a restart straight away ends, in words, or empty when it ends nothing. */
  const cutOff = $derived(
    [upd.runs ? `the ${plural(upd.runs, 'run')} it started` : '', upd.hosted ? `the ${plural(upd.hosted, 'session')} it hosts` : ''].filter(Boolean).join(' and '),
  );
  const words = $derived(updating === 'roll-back' ? ['--roll-back', '--restart-daemon', '--yes'] : ['--restart-daemon', '--yes']);
</script>

<Dialog
  title={updating === 'install' ? `Install ${upd.available} and restart the daemon?` : 'Roll back and restart the daemon?'}
  why={(updating === 'install'
    ? `The daemon restarts on ${upd.available}${upd.channel === 'installer' ? ', running the MSI while it is down' : ', and if that version does not start, the one before is put back'}.`
    : 'The daemon restarts on the version before this one.')
    + (cutOff
      ? ` Waiting lets ${cutOff} carry on until the runs have finished, and nothing new starts meanwhile; restarting now ends ${cutOff} at once.`
      : ' Nothing is running that a restart would end.')
    + ' This page comes back when the daemon does.'}
  actions={[
    { label: 'Restart when its runs finish', cmd: lineFor('update', words), onpress: () => onchoose(words) },
    { label: 'Restart now', kind: 'danger', cmd: lineFor('update', [...words, '--now']), onpress: () => onchoose([...words, '--now']) },
  ]}
  {onclose}
/>
