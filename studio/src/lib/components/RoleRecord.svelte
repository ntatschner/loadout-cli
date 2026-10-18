<script lang="ts">
  // How a team's roles have been doing, per role and model, dearest first.
  // Folded, and read only when opened: it reads every journal on this
  // machine, which is not worth doing for somebody who never looks.
  import { money } from '../teams';

  /** [role, model, runs, total, a run, reports taken %, refused a run]. */
  type Row = [string, string | null, number, number, number, number, number];

  interface Props {
    /** Reads the rows when the record is first opened. */
    read: () => Promise<Row[]>;
    title?: string;
    /** Refusals in a run at or above this are marked high. */
    refusedHigh?: number;
    note?: string;
    open?: boolean;
  }

  let { read, title = 'How the roles have been doing', refusedHigh = 10, note = 'Which model a node ran on has only been recorded since this was added; older runs say not recorded.', open = false }: Props = $props();

  let rows = $state<Row[] | null>(null);
  let problem = $state<string | null>(null);
  let reading = false;
  const HEADS = ['Role', 'Model', 'Runs', 'In all', 'A run', 'Reports taken', 'Refused a run'];

  async function toggled(event: Event) {
    if (!(event.currentTarget as HTMLDetailsElement).open || reading) {
      return;
    }

    reading = true;

    try {
      rows = (await read()).slice().sort((a, b) => b[3] - a[3]);
    } catch (error) {
      problem = (error as Error).message;
    }
  }

  const cells = (row: Row) => [row[0], row[1] ?? 'not recorded', row[2], money(row[3]), money(row[4]), `${row[5]}%`, row[6]];
</script>

<details class="ls-roles" {open} ontoggle={toggled}>
  <summary>{title}</summary>
  <div class="body">
    {#if problem}
      <p class="ls-said no" role="alert">✕ {problem}</p>
    {:else if !rows}
      <p class="ls-note" role="status">Reading every journal on this machine…</p>
    {:else}
      <div class="ls-depths roles-table">
        <table class="ls-xch">
          <caption>Dearest first</caption>
          <thead><tr>{#each HEADS as head, i (head)}<th scope="col" class={i >= 2 ? 'r' : undefined}>{head}</th>{/each}</tr></thead>
          <tbody>
            {#each rows as row, n (n)}
              <tr>
                {#each cells(row) as cell, i (i)}
                  <td data-h={HEADS[i]} class={i >= 2 ? 'r' : undefined}><span>{String(cell)}</span>{#if i === 6 && row[6] >= refusedHigh}<span class="high">high</span>{/if}</td>
                {/each}
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
      <p class="ls-note">{note}</p>
    {/if}
  </div>
</details>
