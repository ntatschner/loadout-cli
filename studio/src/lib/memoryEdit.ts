// A memory topic's edit and its clash: the terminal line a save stands for,
// and which lines on disk the edit does not have.
import { lineFor } from './commandLine';

/** The terminal equivalent of saving: memory replace, expecting the version the edit was read at. */
export function replaceLine(topic: string, project: string, scope: string, version: string): string {
  return lineFor('memory replace', [topic, '--project', project, ...(scope && scope !== 'project' ? ['--scope', scope] : []), '--file', '<path>', '--expect', version]);
}

export function linesOf(text: string): string[] {
  return String(text ?? '').replace(/\n$/, '').split('\n');
}

/** Each line on disk, and whether it is only on disk: the edit has no such line. */
export function diskLines(disk: string, mine: string): { text: string; only: boolean }[] {
  const have = new Set(linesOf(mine));

  return linesOf(disk).map((text) => ({ text, only: !have.has(text) }));
}
