// The palette's one Ask conversation: it lasts until somebody clears it, and
// survives the palette closing and opening again. Kept in memory only, so a
// reload starts afresh.
import type { AskReply, AskTurn } from './ask';

export class AskConversation {
  turns = $state<AskTurn[]>([]);
  asking = $state(false);
  #next = 1;

  /** Asks, adding the request at once and its reply, or why there was none, when it comes. */
  async ask(query: string, asker: (query: string) => Promise<AskReply>): Promise<void> {
    const id = this.#next++;

    this.turns = [...this.turns, { id, query, reply: null, failed: null }];
    this.asking = true;

    try {
      const reply = await asker(query);

      this.turns = this.turns.map((turn) => (turn.id === id ? { ...turn, reply } : turn));
    } catch (error) {
      const failed = error instanceof Error && error.message ? error.message : 'Loadout could not answer that.';

      this.turns = this.turns.map((turn) => (turn.id === id ? { ...turn, failed } : turn));
    } finally {
      this.asking = false;
    }
  }

  clear(): void {
    this.turns = [];
  }
}

export const conversation = new AskConversation();
