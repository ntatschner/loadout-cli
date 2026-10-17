<script lang="ts">
  // Every reason a run wants you: it asked something, the lead took a round
  // without asking for anything, a node is quiet for three times its usual
  // turn, or its budget is spent or projected to be. Each clears itself, so
  // nothing here is dismissed by hand.
  import Gate from './Gate.svelte';
  import HaloButton from './HaloButton.svelte';
  import Signal from './Signal.svelte';
  import { NEED_REASONS, type NeedItem } from '../teams';


  let { items }: { items: NeedItem[] } = $props();

  const id = `ls-nh-${Math.random().toString(36).slice(2, 8)}`;

  function words(item: NeedItem) {
    const reason = NEED_REASONS[item.reason] ?? NEED_REASONS.asked;

    return (item.text ?? reason.text)
      .replace('{node}', item.node ?? 'a node')
      .replace('{for}', item.quiet ?? 'a while')
      .replace('{detail}', item.detail ?? '');
  }
</script>

<section class="ls-needs" aria-labelledby={id}>
  <h2 class="ls-zone" {id}><span class="no">{items.length}</span>Needs you</h2>
  {#if !items.length}
    <p class="calm">Nothing needs you. Each reason clears itself, so this only ever holds what is still true.</p>
  {:else}
    <ul>
      {#each items as item, index (`${item.run}:${item.reason}:${item.node ?? ''}:${index}`)}
        {@const reason = NEED_REASONS[item.reason] ?? NEED_REASONS.asked}
        <li class="need">
          <div class="top"><a href={item.href ?? '#teams'}>{item.goal}</a><Signal word={reason.word} /></div>
          {#if item.gate}
            <Gate {...item.gate} run={item.run} compact />
          {:else}
            <p>{words(item)}</p>
          {/if}
          <span class="clears">{[item.team, item.run, `clears when ${item.clears ?? reason.clears}`].filter(Boolean).join(' · ')}</span>
          {#if item.actions?.length}
            <div class="ls-answers">
              {#each item.actions as action (action.label)}
                <HaloButton size="small" {...action} />
              {/each}
            </div>
          {/if}
        </li>
      {/each}
    </ul>
  {/if}
</section>
