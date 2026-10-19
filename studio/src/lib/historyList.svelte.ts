// Past conversations as the Sessions page shows them: the shared list, read
// from the daemon's history rather than through a command, because only the
// history knows which of them this daemon is hosting. The query is applied by
// the same fields 'loadout sessions --filter' uses.
import { conversations, type Conversation } from './api';
import { CommandList, listWords } from './commandList.svelte';
import { lineFor } from './commandLine';

/** As many as Studio's history has always read. */
export const HISTORY_LIMIT = 100;

export class HistoryList extends CommandList<Conversation> {
  constructor() {
    super('sessions', 'conversations', 'sessions');
  }

  /** The command the list is the same as. */
  override get from(): string {
    return lineFor('sessions', ['--all', '--limit', String(HISTORY_LIMIT), ...listWords(this.query)]);
  }

  protected override fetch(_where: string[], query: string | null): Promise<Record<string, unknown>> {
    return conversations.query(query ?? '', { limit: HISTORY_LIMIT }) as unknown as Promise<Record<string, unknown>>;
  }
}
