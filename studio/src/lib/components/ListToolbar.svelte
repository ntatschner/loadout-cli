<script lang="ts">
  // The same search, filter and sort controls over every list. The box takes
  // the query typed; the chips are its pieces as the daemon read them, and a
  // chip's × takes that piece away; the menus add a filter or choose a sort.
  // All of them edit the one query string, so they cannot disagree. '/' puts
  // you in the box from anywhere on the page that is not already a field.
  import { onDestroy } from 'svelte';
  import { partSaid, sortOf, withoutPart, withSort, withTerm, type ListShape } from '../lists';
  import { typingIn } from '../pure';

  interface Props {
    /** What the list is, for the box's label: "tasks". */
    noun: string;
    /** The query as it stands. */
    query: string;
    /** What the daemon made of it, or null before the first read. */
    shape: ListShape | null;
    /** How many the query kept, and how many there were before it. */
    shown?: number | null;
    recorded?: number | null;
    /** What the daemon refused about the query, if it did. */
    problem?: string | null;
    onchange: (query: string) => void;
  }

  let { noun, query, shape, shown = null, recorded = null, problem = null, onchange }: Props = $props();

  const id = `ls-lt-${Math.random().toString(36).slice(2, 7)}`;
  let box = $state<HTMLInputElement | undefined>();
  let typed = $state('');
  let timer: ReturnType<typeof setTimeout> | undefined;

  // The box follows the query when it changes from elsewhere: a chip, a
  // menu, a reset, or the first read.
  $effect(() => {
    typed = query;
  });

  const parts = $derived(shape?.parts ?? []);
  const sort = $derived(sortOf(parts));
  const choices = $derived((shape?.fields ?? []).filter((field) => field.type === 'choice'));
  const fields = $derived(shape?.fields ?? []);
  const atDefault = $derived(query.trim() === (shape?.defaultQuery ?? '').trim());
  const hidden = $derived(shown !== null && recorded !== null ? recorded - shown : 0);

  function send(next: string) {
    clearTimeout(timer);
    onchange(next.trim());
  }

  function input(event: Event) {
    typed = (event.currentTarget as HTMLInputElement).value;
    clearTimeout(timer);
    // Each keystroke would be a read of the list; wait for a pause.
    timer = setTimeout(() => send(typed), 350);
  }

  function keys(event: KeyboardEvent) {
    if (event.key === 'Enter') {
      event.preventDefault();
      send(typed);
    } else if (event.key === 'Escape' && typed) {
      event.preventDefault();
      typed = query;
    }
  }

  function slash(event: KeyboardEvent) {
    if (event.key === '/' && !event.ctrlKey && !event.metaKey && !event.altKey && !typingIn(event.target)) {
      event.preventDefault();
      box?.focus();
      box?.select();
    }
  }

  function picked(event: Event, apply: (value: string) => string) {
    const select = event.currentTarget as HTMLSelectElement;
    const value = select.value;

    if (value !== '') {
      send(apply(value));
    }

    select.value = '';
  }

  onDestroy(() => clearTimeout(timer));
</script>

<svelte:window onkeydown={slash} />

<div class="ls-ltool" role="search" aria-label={`Filter and sort the ${noun}`}>
  <div class="row">
    <label class="lab" for={id}>Find {noun}</label>
    <input
      bind:this={box}
      {id}
      class="ls-text q"
      type="search"
      value={typed}
      placeholder={`Type to filter, or state:open sort:-updated`}
      autocomplete="off"
      spellcheck="false"
      aria-keyshortcuts="/"
      aria-invalid={problem ? 'true' : undefined}
      aria-describedby={problem ? `${id}-problem` : undefined}
      oninput={input}
      onkeydown={keys}
    />
    {#if choices.length}
      <select class="ls-text menu" aria-label="Add a filter" onchange={(event) => picked(event, (term) => withTerm(parts, term))}>
        <option value="">Add a filter…</option>
        {#each choices as field (field.name)}
          <optgroup label={field.name}>
            {#each field.choices as choice (choice)}
              <option value={`${field.name}:${choice}`}>{field.name} is {choice}</option>
              <option value={`-${field.name}:${choice}`}>{field.name} is not {choice}</option>
            {/each}
          </optgroup>
        {/each}
      </select>
    {/if}
    {#if fields.length}
      <select class="ls-text menu" aria-label="Sort by" value={sort} onchange={(event) => send(withSort(parts, (event.currentTarget as HTMLSelectElement).value))}>
        <option value="">Default order</option>
        {#each fields as field (field.name)}
          <option value={field.name}>{field.name}</option>
          <option value={`-${field.name}`}>{field.name}, descending</option>
        {/each}
        {#if sort && !fields.some((field) => field.name === sort || `-${field.name}` === sort)}<option value={sort}>{sort}</option>{/if}
      </select>
    {/if}
  </div>

  {#if problem}
    <p class="problem" id={`${id}-problem`} role="alert">{problem}</p>
  {/if}

  {#if parts.length || !atDefault}
    <div class="chips">
      {#each parts as part, index (`${index}:${part}`)}
        <span class="chip"><span class="said">{partSaid(part)}</span><button type="button" aria-label={`Remove ${partSaid(part)}`} onclick={() => send(withoutPart(parts, index))}>×</button></span>
      {/each}
      {#if shape && !atDefault}
        <button type="button" class="reset" onclick={() => send(shape.defaultQuery)}>Reset to default</button>
      {/if}
    </div>
  {/if}

  {#if hidden > 0}
    <p class="count" role="status">
      Showing {shown} of {recorded}. {#if shown === 0}Nothing matches.{/if}
      <button type="button" class="clear" onclick={() => send('')}>Show all {recorded}</button>
    </p>
  {/if}
</div>

<style>
  .ls-ltool { display: grid; gap: 8px; margin-bottom: 14px; }
  .row { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
  .row .lab { flex-basis: 100%; }
  .q { flex: 1 1 22ch; max-width: 56ch; width: auto; font-family: var(--font-mono); font-size: 13px; }
  .menu { flex: 0 1 auto; width: auto; max-width: 26ch; }
  .chips { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; }
  .chip { display: inline-flex; align-items: center; gap: 4px; padding: 1px 4px 1px 8px; border-radius: var(--radius-chip); box-shadow: inset 0 0 0 1px var(--control-edge); font-family: var(--font-mono); font-size: 12px; color: var(--ink); }
  .chip button { border: 0; background: none; color: var(--ink-soft); font: inherit; cursor: pointer; padding: 0 4px; }
  .chip button:hover, .chip button:focus-visible { color: var(--ink-strong); }
  .reset, .clear { border: 0; background: none; padding: 0; color: var(--accent); font: inherit; font-size: 13px; text-decoration: underline; cursor: pointer; }
  .count { margin: 0; font-size: 13px; color: var(--ink-soft); }
  .problem { margin: 0; font-size: 13px; color: var(--bad-text); }
</style>
