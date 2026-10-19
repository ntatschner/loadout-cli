<script lang="ts">
  // One layer of an idea's plan: its options side by side, with what is for
  // and against each, to be compared. Choose one, keep or drop the layer, or
  // ask for it to be reworked in a sentence. An addition is drawn the same
  // way, without options. Once the idea is accepted it offers nothing.
  import { onDestroy } from 'svelte';
  import HaloButton from './HaloButton.svelte';
  import Glyph from './Glyph.svelte';
  import { lineFor } from '../commandLine';

  interface Option {
    id: string;
    title: string;
    detail?: string;
    pros?: string[];
    cons?: string[];
    recommended?: boolean;
  }

  interface Props {
    idea: string;
    where: string[];
    accepted?: boolean;
    disabledReason?: string;
    layer?: { id: string; name: string; purpose?: string; chosen?: string | null; verdict?: string; options: Option[] };
    addition?: { id: string; title: string; why?: string; verdict?: string };
    onchoose?: (option: string) => void;
    onverdict?: (verdict: 'Keep' | 'Drop') => void;
    onimprove?: (request: string) => void;
    /** Told whether a rework request is written and not sent. */
    onrework?: (pending: boolean) => void;
  }

  let { idea, where, accepted = false, disabledReason = undefined, layer = undefined, addition = undefined, onchoose = undefined, onverdict = undefined, onimprove = undefined, onrework = undefined }: Props = $props();

  const piece = $derived(layer ?? addition!);
  const id = $derived(piece.id);
  const name = $derived(layer ? layer.name : (addition?.title ?? ''));
  // idea show writes the verdict in lower case; it is compared and shown capitalised.
  const verdict = $derived(((word) => word.charAt(0).toUpperCase() + word.slice(1).toLowerCase())(String(piece.verdict || 'Undecided')));
  const verdictKind = $derived(({ Keep: 'good', Improve: 'needs' } as Record<string, string>)[verdict] ?? '');

  let reworking = $state(false);
  let request = $state('');
  let said = $state('');
  const area = `rw-${Math.random().toString(36).slice(2, 7)}`;

  // Written in the open box and not sent: the page's Next button goes to Send it back.
  // A closed box is put away, so it does not count.
  $effect(() => {
    onrework?.(reworking && !!request.trim());
  });

  onDestroy(() => onrework?.(false));

  function choose(option: Option) {
    onchoose?.(option.id);
    said = `✓ Chose ${option.id} for ${id}`;
  }

  function judge(to: 'Keep' | 'Drop') {
    onverdict?.(to);
    said = `✓ ${to === 'Keep' ? 'Kept' : 'Dropped'} ${id}`;
  }

  function sendBack() {
    onimprove?.(request.trim());
    reworking = false;
    said = `✓ Sent ${id} back to be reworked`;
    request = '';
  }
</script>

<section class={['ls-layer', addition ? 'addition' : '', verdict === 'Drop' ? 'dropped' : ''].filter(Boolean).join(' ')} aria-label={`${addition ? 'Addition' : 'Layer'} ${id}: ${name}`}>
  <div class="lh"><span class="id">{id}</span><h4>{name}</h4><span class={`ls-tag ${verdictKind}`}>verdict: {verdict.toLowerCase()}</span></div>
  {#if addition}
    <p class="why">{addition.why ?? ''}</p>
  {:else if layer?.purpose}
    <p class="purpose">{layer.purpose}</p>
  {/if}

  {#if layer}
    <div class="opts" data-next={`choose:${layer.id}`}>
      {#each layer.options as option (option.id)}
        {@const chosen = layer.chosen === option.id}
        <article class={['card', chosen ? 'chosen' : ''].filter(Boolean).join(' ')} aria-label={`${option.title}${chosen ? ', chosen' : ''}`}>
          <div class="ct"><span class="id">{option.id}</span><b>{option.title}</b>{#if option.recommended}<span class="ls-tag accent">recommended</span>{/if}{#if chosen}<span class="ls-tag good"><Glyph name="ok" />chosen</span>{/if}</div>
          {#if option.detail}<p>{option.detail}</p>{/if}
          {#if option.pros?.length || option.cons?.length}
            <ul>
              {#each option.pros ?? [] as pro, index (`p${index}`)}<li class="pro"><i aria-hidden="true">+</i><span><span class="ls-vh">For: </span>{pro}</span></li>{/each}
              {#each option.cons ?? [] as con, index (`c${index}`)}<li class="con"><i aria-hidden="true">−</i><span><span class="ls-vh">Against: </span>{con}</span></li>{/each}
            </ul>
          {/if}
          {#if !chosen && !accepted}
            <HaloButton label="Choose this" size="small" cmd={lineFor('idea choose', [idea, layer.id, option.id, ...where])} {disabledReason} onpress={() => choose(option)} />
          {/if}
        </article>
      {/each}
    </div>
  {/if}

  {#if !accepted}
    <div class="acts" data-next={`verdict:${id}`}>
      <HaloButton label="Keep it" size="small" glyph="✓" cmd={lineFor('idea keep', [idea, id, ...where])} disabledReason={disabledReason ?? (verdict === 'Keep' ? 'kept already' : undefined)} onpress={() => judge('Keep')} />
      <HaloButton label="Drop it" size="small" glyph="✕" cmd={lineFor('idea drop', [idea, id, ...where])} disabledReason={disabledReason ?? (verdict === 'Drop' ? 'dropped already' : undefined)} onpress={() => judge('Drop')} />
      <HaloButton label="Ask for it to be reworked" size="small" glyph="↻" cmd="says what should change, then sends it" {disabledReason} onpress={() => (reworking = !reworking)} />
    </div>
    <div class={['rework', reworking ? 'on' : ''].filter(Boolean).join(' ')}>
      <textarea class="ls-text" id={area} rows="2" placeholder="What should change, in a sentence" aria-label={`What should change about ${id}`} bind:value={request}></textarea>
      <div>
        <HaloButton label="Send it back" next={`rework:${id}`} size="small" kind="needs" cmd={lineFor('idea improve', [idea, id, `--request=${request.trim() || '<what should change>'}`, ...where])} disabledReason={request.trim() ? disabledReason : 'say what should change'} onpress={sendBack} />
      </div>
    </div>
  {/if}
  <p class="ls-said" role="status">{said}</p>
</section>
