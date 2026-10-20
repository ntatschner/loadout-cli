<script lang="ts">
  // One layer of an idea's plan: its options side by side, with what is for
  // and against each, to be compared. Choose one, keep or drop the layer, or
  // ask for it to be reworked in a sentence. An addition is drawn the same
  // way, without options. Once the idea is accepted it offers nothing.
  //
  // A choice or a verdict shows the moment it is made, and goes back to what
  // it was if the command fails. Nothing here changes size when it does: the
  // button and the chosen marker share one slot, and the status line keeps
  // its height when empty.
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
    /** Each resolves true once the change is recorded and read back, and false when it was not. */
    onchoose?: (option: string) => Promise<boolean>;
    onverdict?: (verdict: 'Keep' | 'Drop') => Promise<boolean>;
    onimprove?: (request: string) => Promise<boolean>;
    /** Told whether a rework request is written and not sent. */
    onrework?: (pending: boolean) => void;
  }

  let { idea, where, accepted = false, disabledReason = undefined, layer = undefined, addition = undefined, onchoose = undefined, onverdict = undefined, onimprove = undefined, onrework = undefined }: Props = $props();

  const piece = $derived(layer ?? addition!);
  const id = $derived(piece.id);
  const name = $derived(layer ? layer.name : (addition?.title ?? ''));
  // Made here and not yet read back: shown in place of what the idea says until it is.
  let choosing = $state<string | null>(null);
  let judging = $state<'Keep' | 'Drop' | 'Improve' | null>(null);
  const chosen = $derived(choosing ?? layer?.chosen ?? null);

  // idea show writes the verdict in lower case; it is compared and shown capitalised.
  const verdict = $derived(judging ?? ((word) => word.charAt(0).toUpperCase() + word.slice(1).toLowerCase())(String(piece.verdict || 'Undecided')));
  const verdictKind = $derived(({ Keep: 'good', Improve: 'needs' } as Record<string, string>)[verdict] ?? '');
  // While one is being recorded the others do nothing, but keep the command written under them: a
  // reason in its place is a line shorter, and every card in the row would shrink with it. The
  // verdict already given does nothing for the same reason; the verdict tag says which it is.
  const busy = $derived(!!choosing || !!judging);

  let reworking = $state(false);
  let request = $state('');
  let said = $state('');
  let failed = $state(false);
  const area = `rw-${Math.random().toString(36).slice(2, 7)}`;

  // Written in the open box and not sent: the page's Next button goes to Send it back.
  // A closed box is put away, so it does not count.
  $effect(() => {
    onrework?.(reworking && !!request.trim());
  });

  onDestroy(() => onrework?.(false));

  async function choose(option: Option) {
    if (choosing || !onchoose) {
      return;
    }

    choosing = option.id;
    said = '';
    const ok = await onchoose(option.id);
    choosing = null;
    failed = !ok;
    said = ok ? `✓ Chose ${option.id} for ${id}` : `✕ ${option.id} was not recorded for ${id}`;
  }

  async function judge(to: 'Keep' | 'Drop' | 'Improve', send: () => Promise<boolean>, done: string) {
    if (judging) {
      return;
    }

    judging = to;
    said = '';
    const ok = await send();
    judging = null;
    failed = !ok;
    said = ok ? `✓ ${done}` : `✕ That was not recorded for ${id}`;
  }

  function sendBack() {
    const text = request.trim();

    reworking = false;
    request = '';
    void judge('Improve', () => onimprove?.(text) ?? Promise.resolve(false), `Sent ${id} back to be reworked`);
  }
</script>

<section class={['ls-layer', addition ? 'addition' : '', verdict === 'Drop' ? 'dropped' : ''].filter(Boolean).join(' ')} aria-label={`${addition ? 'Addition' : 'Layer'} ${id}: ${name}`} data-anchor={`piece:${id}`}>
  <div class="lh"><span class="id">{id}</span><h4>{name}</h4><span class={`ls-tag ${verdictKind}`}>verdict: {verdict.toLowerCase()}</span></div>
  {#if addition}
    <p class="why">{addition.why ?? ''}</p>
  {:else if layer?.purpose}
    <p class="purpose">{layer.purpose}</p>
  {/if}

  {#if layer}
    <div class="opts" data-next={`choose:${layer.id}`}>
      {#each layer.options as option (option.id)}
        {@const picked = chosen === option.id}
        <article class={['card', picked ? 'chosen' : ''].filter(Boolean).join(' ')} aria-label={`${option.title}${picked ? ', chosen' : ''}`} data-anchor={`option:${layer.id}:${option.id}`}>
          <div class="ct"><span class="id">{option.id}</span><b>{option.title}</b>{#if option.recommended}<span class="ls-tag accent">recommended</span>{/if}</div>
          {#if option.detail}<p>{option.detail}</p>{/if}
          {#if option.pros?.length || option.cons?.length}
            <ul>
              {#each option.pros ?? [] as pro, index (`p${index}`)}<li class="pro"><i aria-hidden="true">+</i><span><span class="ls-vh">For: </span>{pro}</span></li>{/each}
              {#each option.cons ?? [] as con, index (`c${index}`)}<li class="con"><i aria-hidden="true">−</i><span><span class="ls-vh">Against: </span>{con}</span></li>{/each}
            </ul>
          {/if}
          {#if !accepted}
            <!-- Both always drawn in one cell, the other one hidden, so choosing changes nothing's size. -->
            <div class="slot">
              <div class={picked ? 'off' : ''} aria-hidden={picked ? 'true' : undefined} inert={picked}>
                <HaloButton label="Choose this" size="small" cmd={lineFor('idea choose', [idea, layer.id, option.id, ...where])} {disabledReason} inactive={busy} onpress={() => void choose(option)} />
              </div>
              <p class={['mark', picked ? '' : 'off'].filter(Boolean).join(' ')} aria-hidden={picked ? undefined : 'true'}><span class="ls-tag good"><Glyph name="ok" />{choosing === option.id ? 'chosen · recording' : 'chosen'}</span></p>
            </div>
          {:else if picked}
            <p class="mark"><span class="ls-tag good"><Glyph name="ok" />chosen</span></p>
          {/if}
        </article>
      {/each}
    </div>
  {/if}

  {#if !accepted}
    <div class="acts" data-next={`verdict:${id}`}>
      <HaloButton label="Keep it" size="small" glyph="✓" cmd={lineFor('idea keep', [idea, id, ...where])} {disabledReason} inactive={busy || verdict === 'Keep'} onpress={() => void judge('Keep', () => onverdict?.('Keep') ?? Promise.resolve(false), `Kept ${id}`)} />
      <HaloButton label="Drop it" size="small" glyph="✕" cmd={lineFor('idea drop', [idea, id, ...where])} {disabledReason} inactive={busy || verdict === 'Drop'} onpress={() => void judge('Drop', () => onverdict?.('Drop') ?? Promise.resolve(false), `Dropped ${id}`)} />
      <HaloButton label="Ask for it to be reworked" size="small" glyph="↻" cmd="says what should change, then sends it" {disabledReason} inactive={busy} onpress={() => (reworking = !reworking)} />
    </div>
    <div class={['rework', reworking ? 'on' : ''].filter(Boolean).join(' ')}>
      <textarea class="ls-text" id={area} rows="2" placeholder="What should change, in a sentence" aria-label={`What should change about ${id}`} bind:value={request}></textarea>
      <div>
        <HaloButton label="Send it back" next={`rework:${id}`} size="small" kind="needs" cmd={lineFor('idea improve', [idea, id, `--request=${request.trim() || '<what should change>'}`, ...where])} disabledReason={request.trim() ? disabledReason : 'say what should change'} inactive={busy} onpress={sendBack} />
      </div>
    </div>
  {/if}
  <p class={['ls-said', 'held', failed ? 'no' : ''].filter(Boolean).join(' ')} role="status">{said}</p>
</section>
