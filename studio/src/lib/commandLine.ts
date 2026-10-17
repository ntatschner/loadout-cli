// Turning a command's form into the words after 'loadout', and back into
// the line somebody would type. The daemon runs exactly these words, so the
// line the page shows and the command that runs cannot differ.
import type { StudioCommand, StudioOption } from './api';

/** What the form holds: one entry per argument name and per option name. */
export type FormValues = Record<string, string | boolean | undefined>;

/** Why the form cannot be run as it is, or null when it can. */
export function problemWith(command: StudioCommand, values: FormValues): string | null {
  for (const argument of command.arguments) {
    const value = text(values[argument.name]);

    if (argument.required && !value) {
      return `${command.path} needs its ${argument.name.toLowerCase()}.`;
    }

    for (const word of argument.many ? lines(value) : value ? [value] : []) {
      if (word.startsWith('-')) {
        return `A ${argument.name.toLowerCase()} cannot start with a dash: the command would read it as an option.`;
      }
    }
  }

  for (const option of command.options) {
    if (option.required && !given(option, values[option.name])) {
      return `${command.path} needs ${option.name}.`;
    }
  }

  return null;
}

function text(value: string | boolean | undefined): string {
  return typeof value === 'string' ? value.trim() : '';
}

function lines(value: string): string[] {
  return value
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean);
}

function given(option: StudioOption, value: string | boolean | undefined): boolean {
  return option.kind === 'flag' || option.kind === 'optional' ? value === true || text(value).length > 0 : text(value).length > 0;
}

/**
 * The words after the command's own name. A value that starts with a dash
 * is joined to its option with '=', because the parser refuses one given
 * as the next word.
 */
export function wordsFor(command: StudioCommand, values: FormValues): string[] {
  const words: string[] = [];

  for (const argument of command.arguments) {
    const value = text(values[argument.name]);

    words.push(...(argument.many ? lines(value) : value ? [value] : []));
  }

  for (const option of command.options) {
    const value = values[option.name];

    switch (option.kind) {
      case 'flag':
        if (value === true) {
          words.push(option.name);
        }
        break;

      case 'optional':
        if (text(value)) {
          words.push(`${option.name}=${text(value)}`);
        } else if (value === true) {
          words.push(option.name);
        }
        break;

      case 'values':
        for (const one of lines(text(value))) {
          words.push(...joined(option.name, one));
        }
        break;

      default:
        if (text(value)) {
          words.push(...joined(option.name, text(value)));
        }
    }
  }

  return words;
}

function joined(name: string, value: string): string[] {
  return value.startsWith('-') ? [`${name}=${value}`] : [name, value];
}

/**
 * Whether these words only read, by the daemon's own rule: the command
 * reads, or is previewed with --dry-run, or is one that acts only when given
 * a particular option and is not given it.
 */
export function readsWith(command: StudioCommand, words: string[]): boolean {
  if (command.reach.effect === 'reads' || words.includes('--dry-run')) {
    return true;
  }

  const acting = command.reach.changesOnlyWith ?? [];

  return command.reach.effect === 'changes' && acting.length > 0 && !words.some((word) => acting.includes(word.split('=')[0]));
}

/** A word as a shell would need it quoted, matching the daemon's own quoting. */
export function quoted(word: string): string {
  return /^[A-Za-z0-9\-_./:=@,+%]+$/.test(word) ? word : `'${word.replaceAll("'", "'\\''")}'`;
}

/** The line as it would be typed, with the prompt. */
export function lineFor(path: string, words: string[]): string {
  return `$ loadout ${[path, ...words.map(quoted)].join(' ')}`;
}
