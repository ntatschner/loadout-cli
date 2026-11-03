<script lang="ts">
  // The "Before this" band: what the project lacks before this idea, one line a
  // gap, one choice a line. Reads and changes through 'task offers' and
  // 'project capabilities', so every button here is a command that can be run
  // as written in a terminal.
  import { untrack } from 'svelte';
  import { readJson, SignedOut } from '../api';
  import { lineFor } from '../commandLine';
  import { studio } from '../state.svelte';
  import { acceptOfferWords, firstOpen, nextStack, stackIn, stackWord, type Offer, type OfferSheet } from '../offers';
  import HaloButton from './HaloButton.svelte';

  interface Props {
    id: string;
    project: string;
    /** Changes whenever the idea does, so its type or links changing reads the band again. */
    revision: string;
    disabledReason?: string;
    run: (path: string, words: string[], undone: string) => Promise<boolean>;
    /** Says how many links to prerequisites this idea has, for the box beside the type picker. */
    onlinks?: (count: number) => void;
  }

  let { id, project, revision, disabledReason, run, onlinks }: Props = $props();

  let sheet = $state<OfferSheet | null>(null);
  let later = $state<string[]>([]);
  let chosen = $state<Record<string, string>>({});
  let names = $state<Record<string, string>>({});

  const where = $derived(['--project', project]);
  const shown = $derived((sheet?.offers ?? []).filter((offer) => !later.includes(offer.check)));
  const first = $derived(firstOpen(sheet?.offers ?? [], later));

  async function load() {
    try {
      sheet = await readJson<OfferSheet>('task offers', [id, ...where]);
      onlinks?.(sheet.links?.length ?? 0);
    } catch (error) {
      // The band is an extra on the page, and a read it could not make is no
      // reason to stand in the way of the idea. Signing out is said elsewhere.
      if (error instanceof SignedOut) {
        studio.signedOut = true;
      }

      sheet = null;
    }
  }

  $effect(() => {
    void id;
    void project;
    void revision;
    untrack(() => void load());
  });

  function typedNames(offer: Offer): string | undefined {
    return offer.check === 'environments' ? (names[offer.check] ?? offer.environments.join(' ')) : undefined;
  }

  const acceptWords = (offer: Offer) => acceptOfferWords(id, where, offer, chosen, typedNames(offer));

  async function accept(offer: Offer) {
    await run('task offers', acceptWords(offer), `The ${offer.check} prerequisite was not made.`);
    await load();
  }

  async function notNeeded(offer: Offer) {
    await run('project capabilities', [project, `--not-needed=${offer.check}`], `${offer.check} was not marked as not needed.`);
    await load();
  }

  async function unlink(prerequisite: string) {
    await run('task requires', [id, prerequisite, ...where, '--remove'], `The link to ${prerequisite} was not taken off.`);
    await load();
  }

  const outgrown = $derived((sheet?.links ?? []).filter((link) => link.outgrown));

  async function lookAgain() {
    await run('project capabilities', [project, '--refresh'], 'The profile was not built again.');
    await load();
  }

  function cycle(offer: Offer) {
    const next = nextStack(offer, chosen);

    if (next) {
      chosen = { ...chosen, [offer.check]: next };
    }
  }

  // O takes the first open offer and S changes its stack, outside text fields,
  // the same rule the page's own key follows.
  function onkey(event: KeyboardEvent) {
    const typing = (event.target as HTMLElement | null)?.closest('input, textarea, select, [contenteditable="true"], .xterm');

    if (!first || disabledReason || typing || event.ctrlKey || event.metaKey || event.altKey || !studio.keys || document.querySelector('.ls-dialog')) {
      return;
    }

    if (event.key === 'o') {
      event.preventDefault();
      void accept(first);
    } else if (event.key === 's' && first.stacks.length > 1) {
      event.preventDefault();
      cycle(first);
    }
  }
</script>

<svelte:window onkeydown={onkey} />

{#if sheet && (shown.length || sheet.unseen.length || outgrown.length)}
  <section class="before" aria-labelledby="before-this">
    <h2 class="ls-zone" id="before-this" data-anchor="zone:before">Before this</h2>
    {#if first}<p class="keys">Press <kbd>o</kbd> to take the first offer{#if first.stacks.length > 1}, <kbd>s</kbd> to change its stack{/if}.</p>{/if}
    <ul>
      {#each shown as offer (offer.check)}
        <li data-check={offer.check}>
          <p class="what"><strong>{offer.name}.</strong> {offer.evidence}</p>
          {#if offer.linked}
            <p class="x">This waits for {offer.prerequisite}.</p>
            <div class="ls-answers flush">
              <HaloButton label="Stop waiting for it" size="small" cmd={lineFor('task requires', [id, offer.prerequisite!, ...where, '--remove'])} {disabledReason} onpress={() => void unlink(offer.prerequisite!)} />
            </div>
          {:else}
            {#if offer.canOffer && stackIn(offer, chosen)}
              <p class="x">For {stackWord(stackIn(offer, chosen)!)}.
                {#if offer.stacks.length > 1}<HaloButton label="Change stack" size="small" onpress={() => cycle(offer)} />{/if}
              </p>
            {/if}
            {#if offer.canOffer && offer.check === 'environments'}
              <label class="x">Environments
                <input class="ls-text" value={names[offer.check] ?? offer.environments.join(' ')} oninput={(event) => (names = { ...names, [offer.check]: (event.currentTarget as HTMLInputElement).value })} />
              </label>
            {/if}
            {#if !offer.canOffer}
              <p class="x">No offer: only Loadout's own templates make one. A task can be linked by hand.</p>
            {/if}
            <div class="ls-answers flush">
              {#if offer.canOffer}
                <HaloButton label={offer.prerequisite ? `Wait for ${offer.prerequisite}` : 'Add it first'} size="small" cmd={lineFor('task offers', acceptWords(offer))} {disabledReason} onpress={() => void accept(offer)} />
              {/if}
              <HaloButton label="Not needed" size="small" cmd={lineFor('project capabilities', [project, `--not-needed=${offer.check}`])} {disabledReason} onpress={() => void notNeeded(offer)} />
              <HaloButton label="Later" size="small" onpress={() => (later = [...later, offer.check])} />
            </div>
          {/if}
        </li>
      {/each}
    </ul>
    {#each outgrown as link (link.prerequisite)}
      <div class="outgrown" data-outgrown={link.prerequisite}>
        <p class="what">This still waits for {link.prerequisite}, which its type no longer depends on.</p>
        <div class="ls-answers flush">
          <HaloButton label="Stop waiting for it" size="small" cmd={lineFor('task requires', [id, link.prerequisite, ...where, '--remove'])} {disabledReason} onpress={() => void unlink(link.prerequisite)} />
        </div>
      </div>
    {/each}
    {#if sheet.unseen.length}
      <div class="unseen">
        {#each sheet.unseen as one (one.check)}
          <p class="x"><strong>{one.check}</strong> could not be looked at, so it holds nothing. {one.hint}</p>
        {/each}
        <HaloButton label="Look again, asking GitHub" size="small" cmd={lineFor('project capabilities', [project, '--refresh'])} {disabledReason} onpress={() => void lookAgain()} />
      </div>
    {/if}
    {#each sheet.notes as note (note)}<p class="x">{note}</p>{/each}
  </section>
{/if}

<style>
  .before ul {
    list-style: none;
    margin: 0;
    padding: 0;
    display: grid;
    gap: 12px;
  }

  .before p {
    margin: 0 0 4px;
  }

  .x {
    color: var(--muted);
  }

  .keys {
    color: var(--muted);
  }

  .unseen {
    margin-top: 12px;
  }
</style>
