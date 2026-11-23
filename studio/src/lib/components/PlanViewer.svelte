<script lang="ts">
  // An idea's plan in a dialog. Read only. Opened with a confirm, it is the
  // step Accept goes through: what is shown is a preview of exactly what
  // accepting writes, and Confirm sends the hash of what was shown, so a plan
  // that changed while it was open is refused rather than written unread.
  import Dialog from './Dialog.svelte';
  import HaloButton from './HaloButton.svelte';
  import PlanView from './PlanView.svelte';
  import { holdWords, type PlanRead } from '../plans';

  interface Props {
    id: string;
    where: string[];
    doneWhen?: string[];
    title?: string;
    /** For the confirm step: what Confirm runs, given the words that hold it to what was read. */
    confirm?: { cmd: string; disabledReason?: string; onconfirm: (hold: string[]) => unknown };
    onclose: () => void;
  }

  let { id, where, doneWhen = [], title = undefined, confirm = undefined, onclose }: Props = $props();

  let plan = $state<PlanRead | null>(null);

  const confirmReason = $derived(
    confirm?.disabledReason
      ?? (!plan ? 'the plan is still being read' : plan.state !== 'preview' ? 'there is no preview to confirm' : undefined),
  );

  const actions = $derived(
    confirm
      ? [{
          label: 'Confirm and accept',
          glyph: '✓',
          solid: !confirmReason,
          size: 'small' as const,
          cmd: confirm.cmd,
          disabledReason: confirmReason,
          onpress: () => confirm.onconfirm(holdWords(plan)),
        }]
      : [],
  );
</script>

<Dialog
  title={title ?? (confirm ? 'Read the plan, then confirm' : 'The plan')}
  label="Plan"
  extraClass="wide ls-planviewer"
  cancelLabel="Back"
  {actions}
  {onclose}
>
  <PlanView {id} {where} {doneWhen} onread={(read) => (plan = read)} />
  {#if !confirm}
    <div class="acts close"><HaloButton label="Close" size="small" onpress={onclose} /></div>
  {/if}
</Dialog>
