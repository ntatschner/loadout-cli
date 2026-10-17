<script lang="ts">
  // The command bar along the bottom of Pure Focus: type an action, a place
  // or a loadout command. A combobox; every match shows what it runs before
  // Enter. Escape empties it and gives the keyboard back.
  import type { PureCommand } from '../pure';

  interface Props {
    /** The matches for what is typed. */
    source: (typed: string) => PureCommand[];
    placeholder?: string;
    /** Told when it is done with, chosen or let go, to give the keyboard back. */
    ondone?: () => void;
  }

  let { source, placeholder = 'Type an action, a place or a loadout command', ondone = undefined }: Props = $props();

  const listId = `ls-cb-${Math.random().toString(36).slice(2, 7)}`;
  let input = $state<HTMLInputElement | undefined>();
  let typed = $state('');
  let open = $state(false);
  let active = $state(0);
  let closing: ReturnType<typeof setTimeout> | null = null;

  const rows = $derived(open ? source(typed).slice(0, 40) : []);

  $effect(() => {
    if (active >= rows.length) {
      active = Math.max(0, rows.length - 1);
    }
  });

  export function focus() {
    input?.focus();
  }

  export function isOpen(): boolean {
    return open;
  }

  function choose(index = active) {
    const row = rows[index];

    if (!row) {
      return;
    }

    open = false;
    typed = '';
    ondone?.();
    row.run?.();
  }

  function keys(event: KeyboardEvent) {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();

      if (rows.length) {
        active = (active + (event.key === 'ArrowDown' ? 1 : -1) + rows.length) % rows.length;
        document.getElementById(`${listId}-${active}`)?.scrollIntoView?.({ block: 'nearest' });
      }
    } else if (event.key === 'Enter') {
      event.preventDefault();
      choose();
    } else if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      typed = '';
      open = false;
      ondone?.();
    }
  }
</script>

<div class="ls-cmdbar">
  <kbd class="ls-kbd">:</kbd>
  <input
    bind:this={input}
    bind:value={typed}
    type="text"
    role="combobox"
    aria-expanded={open ? 'true' : 'false'}
    aria-controls={listId}
    aria-autocomplete="list"
    aria-activedescendant={open && rows.length ? `${listId}-${active}` : undefined}
    aria-label="Command bar"
    aria-keyshortcuts=":"
    {placeholder}
    autocomplete="off"
    spellcheck="false"
    onfocus={() => {
      if (closing) {
        clearTimeout(closing);
      }

      open = true;
    }}
    onblur={() => (closing = setTimeout(() => (open = false), 120))}
    oninput={() => {
      active = 0;
      open = true;
    }}
    onkeydown={keys}
  />
  <ul role="listbox" id={listId} aria-label="Matches" hidden={!open}>
    {#each rows as row, index (index)}
      <li
        role="option"
        id={`${listId}-${index}`}
        aria-selected={index === active ? 'true' : 'false'}
        onmousedown={(event) => {
          event.preventDefault();
          choose(index);
        }}
      >
        <span class="k">{row.kind}</span><span class="l">{row.label}</span>{#if row.cmd}<code>{row.cmd}</code>{/if}
      </li>
    {:else}
      <li role="option" class="none" aria-disabled="true" aria-selected="false">{typed.trim() ? `Nothing matches “${typed.trim()}”.` : 'Start typing.'}</li>
    {/each}
  </ul>
  <span class="hints" aria-hidden="true">
    <span><kbd class="ls-kbd">:</kbd>command</span>
    <span><kbd class="ls-kbd">?</kbd>keys</span>
    <span><kbd class="ls-kbd">G</kbd>go</span>
    <span><kbd class="ls-kbd">F6</kbd>panes</span>
  </span>
</div>
