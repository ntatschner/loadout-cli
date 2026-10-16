// Raising the most urgent waiting row and receding the rest, so the eye goes
// to it; colour is not the only cue, since the raised row also moves and
// carries its own word.

export { rankUrgent } from './signals';

/** Raises row `risingIndex` (ls-rise) and recedes the rest (ls-recede). Pass -1 to clear. */
export function applyUrgency(rows: HTMLElement[], risingIndex: number, level: 'needs' | 'problem' = 'needs'): void {
  rows.forEach((row, index) => {
    row.classList.toggle('ls-rise', index === risingIndex);
    row.classList.toggle('problem', index === risingIndex && level === 'problem');
    row.classList.toggle('ls-recede', risingIndex >= 0 && index !== risingIndex);
  });
}

/** The class a row in a ranked list takes: what applyUrgency would give it. */
export function urgencyClass(index: number, risingIndex: number, level: 'needs' | 'problem' = 'needs'): string {
  if (risingIndex < 0) {
    return '';
  }

  if (index === risingIndex) {
    return level === 'problem' ? 'ls-rise problem' : 'ls-rise';
  }

  return 'ls-recede';
}
