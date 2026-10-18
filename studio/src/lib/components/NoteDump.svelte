<script lang="ts">
  // One batch of notes pasted from elsewhere, after an agent split it into
  // ideas and tasks. Tick the pieces to record; recorded ones are ticked,
  // greyed and say so.
  import HaloButton from './HaloButton.svelte';
  import { lineFor } from '../commandLine';
  import { applyWords, type DumpItem } from '../ideas';

  interface Props {
    dump: { id: string; source?: string; captured?: string; split?: string | null; items: DumpItem[] };
    disabledReason?: string;
    onapply?: (words: string[]) => void;
    onsplit?: () => void;
    onshow?: () => void;
  }

  let { dump, disabledReason = undefined, onapply = undefined, onsplit = undefined, onshow = undefined }: Props = $props();

  // Every unrecorded piece starts ticked.
  let unticked = $state<Record<number, boolean>>({});
  const picked = $derived(dump.items.filter((one) => !one.recorded && !unticked[one.number]).map((one) => one.number));
  const open = $derived(dump.items.filter((one) => !one.recorded).length);
  const words = $derived(applyWords(dump.id, dump.items, picked));
  const when = (at: string | null | undefined) => (at ? new Date(at).toLocaleString([], { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }) : '');
</script>

<section class="ls-dump" aria-label={`Notes ${dump.id}`}>
  <div class="dh"><b>{dump.source || dump.id}</b><span>{dump.id} · {when(dump.captured)}{dump.split ? ` · split ${when(dump.split)}` : ' · not split yet'}</span></div>
  <ol>
    {#each dump.items as item (item.number)}
      <li class={item.recorded ? 'recorded' : ''}>
        <label>
          <input type="checkbox" name={`piece-${item.number}`} disabled={!!item.recorded} checked={!!item.recorded || !unticked[item.number]} onchange={(event) => (unticked = { ...unticked, [item.number]: !(event.currentTarget as HTMLInputElement).checked })} />
          <span class="no">{item.number}</span>
          <span class="t">{item.title}</span>
          {#if item.excerpt}<span class="x">{item.excerpt}</span>{/if}
          <span class={`ls-tag ${item.recorded ? 'good' : ''}`}>{item.recorded ? `${item.kind} · recorded` : item.kind}</span>
        </label>
      </li>
    {/each}
  </ol>
  <div class="acts">
    <HaloButton label={picked.length ? `Record ${picked.length}${picked.length === open ? ', all of them' : ''}` : 'Record them'} size="small" solid={!disabledReason && picked.length > 0} cmd={lineFor('idea dump apply', words)} disabledReason={disabledReason ?? (picked.length ? undefined : 'tick a piece first')} onpress={() => onapply?.(words)} />
    <HaloButton label="Split it again" size="small" glyph="↻" cmd={lineFor('idea dump split', [dump.id])} {disabledReason} onpress={() => onsplit?.()} />
    <HaloButton label="Read it whole" size="small" cmd={`${lineFor('idea dump show', [dump.id])} · reads only`} onpress={() => onshow?.()} />
  </div>
</section>
