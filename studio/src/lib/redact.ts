// Studio's copy of the launcher's SecretRedactor
// (src/Loadout.Core/Security/SecretRedactor.cs), rule for rule. Both are held
// to the same exact-output cases in tests/shared/redaction-cases.json, so a
// rule changed on one side and not the other fails a suite.
//
// Everything a failure shows or copies passes through here first: the cause,
// the command, the output. A command that fails is often failing over a
// credential, and that is exactly when its output is most likely to carry one.
//
// The C# patterns each carry a one-second timeout against runaway matching.
// JavaScript has none, so this caps the text instead: only the last KEPT
// characters are read, the same number Details keeps of a failure's output.

const PLACEHOLDER = '[redacted]';

/** How much of a text is kept and read: the last 64 KiB, counted in characters. */
export const KEPT = 64 * 1024;

// A quoted value taken whole, or else everything up to the next whitespace.
// An unclosed quote runs to the end of its line; inside either quote, an
// escaped quote (\" `" "" and \' `' '') is part of the value.
const VALUE = String.raw`(?:"(?:\\"|` + '`' + String.raw`"|""|[^"\r\n])*"?|'(?:\\'|` + '`' + String.raw`'|''|[^'\r\n])*'?|\S+)`;

const URL_CREDENTIALS = /([a-zA-Z][a-zA-Z0-9+.-]*:\/\/)[^/\s:@]+:[^/\s@]+@/g;

const AUTHORIZATION_HEADER = /\b(Authorization:\s*(?:Bearer|Basic|token))\s+\S+/gi;

const FLAG_SECRET = new RegExp(
  String.raw`(?<![A-Za-z0-9_-])(--(?:[A-Za-z0-9_-]*(?:TOKEN|SECRET|PASSWORD|PASSWD|CREDENTIAL|APIKEY)|[A-Za-z0-9_-]*[-_]KEY(?![A-Za-z0-9]))[A-Za-z0-9_-]*(?:=|[ \t]+(?!-)))` + VALUE,
  'gi',
);

const ASSIGNED_SECRET = new RegExp(
  String.raw`\b([A-Za-z_][A-Za-z0-9_]*(?:KEY|TOKEN|SECRET|PASSWORD|PASSWD|CREDENTIAL|APIKEY)[A-Za-z0-9_]*\s*[=:]\s*)` + VALUE,
  'gi',
);

const BARE_SECRET = new RegExp(String.raw`(?<![A-Za-z0-9_])((?:TOKEN|SECRET|PASSWORD|PASSWD|CREDENTIAL|APIKEY)[A-Z0-9_]*=)` + VALUE, 'g');

const JSON_SECRET_FIELD = /("(?:[a-zA-Z0-9_]*(?:key|token|secret|password|credential)[a-zA-Z0-9_]*)"\s*:\s*")[^"]*"/gi;

const KNOWN_TOKEN_SHAPE = /\b(?:sk-ant-|sk-|ghp_|gho_|ghs_|github_pat_|xox[baprs]-)[A-Za-z0-9_-]{8,}/g;

/**
 * The last KEPT characters of a text, starting at a line where it had to be
 * cut, so the kept part never opens halfway through a value whose name was
 * cut away. Says whether anything was cut.
 */
export function keepTail(text: string): { text: string; cut: boolean } {
  if (text.length <= KEPT) {
    return { text, cut: false };
  }

  const tail = text.slice(-KEPT);
  const line = tail.indexOf('\n');

  return { text: line >= 0 ? tail.slice(line + 1) : tail, cut: true };
}

/** The text with recognisable credentials replaced, as the launcher's redactor does it. */
export function redact(text: string | null | undefined): string {
  if (!text) {
    return '';
  }

  let result = keepTail(text).text;

  result = result.replace(URL_CREDENTIALS, `$1${PLACEHOLDER}@`);
  result = result.replace(AUTHORIZATION_HEADER, `$1 ${PLACEHOLDER}`);
  result = result.replace(FLAG_SECRET, `$1${PLACEHOLDER}`);
  result = result.replace(ASSIGNED_SECRET, `$1${PLACEHOLDER}`);
  result = result.replace(BARE_SECRET, `$1${PLACEHOLDER}`);
  result = result.replace(JSON_SECRET_FIELD, `$1${PLACEHOLDER}"`);
  result = result.replace(KNOWN_TOKEN_SHAPE, PLACEHOLDER);

  return result;
}

/** Whether redacting the text changed it: a copy that lost a secret says so. */
export function hadSecret(text: string | null | undefined): boolean {
  return !!text && redact(text) !== keepTail(text).text;
}
