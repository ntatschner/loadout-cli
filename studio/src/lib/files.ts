// Reading what a run delivered without ever treating it as a page: Markdown
// parsed into a small tree of blocks and runs that the viewer draws as
// elements, code split into tokens, a CSV split into cells. Nothing from a
// file is ever handed to the browser as HTML, and only http and https links
// are live. Ported from the design system's Outbox renderers.

export type FileKind = 'markdown' | 'csv' | 'json' | 'image' | 'text' | 'code' | 'binary';

const KIND: Record<string, FileKind> = {
  md: 'markdown',
  markdown: 'markdown',
  csv: 'csv',
  tsv: 'csv',
  json: 'json',
  png: 'image',
  jpg: 'image',
  jpeg: 'image',
  gif: 'image',
  webp: 'image',
  svg: 'image',
  txt: 'text',
  log: 'text',
};

const CODE = new Set(['cs', 'ts', 'js', 'mjs', 'svelte', 'py', 'ps1', 'sh', 'yaml', 'yml', 'toml', 'xml', 'csproj', 'html', 'css', 'go', 'rs', 'java', 'kt', 'sql', 'ini', 'props']);

export function extension(path: string): string {
  return /\.([A-Za-z0-9]+)$/.exec(path)?.[1].toLowerCase() ?? '';
}

export function baseName(path: string): string {
  return path.split(/[\\/]/).pop() ?? path;
}

export function fileKind(path: string): FileKind {
  const ext = extension(path);

  return KIND[ext] ?? (CODE.has(ext) ? 'code' : ext ? 'binary' : 'text');
}

export function sizeOf(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined) {
    return '';
  }

  return bytes < 1024 ? `${bytes} B` : bytes < 1048576 ? `${(bytes / 1024).toFixed(bytes < 10240 ? 1 : 0)} KB` : `${(bytes / 1048576).toFixed(1)} MB`;
}

/* ---------- inline runs ---------- */

export type Run =
  | { t: 'text'; text: string }
  | { t: 'code'; text: string }
  | { t: 'strong'; runs: Run[] }
  | { t: 'em'; runs: Run[] }
  /** A link that may be followed: http or https only. */
  | { t: 'link'; href: string; runs: Run[] }
  /** Anything else a link pointed at, shown as words and its target, not followed. */
  | { t: 'dead'; href: string; runs: Run[] };

export function inline(text: string): Run[] {
  const runs: Run[] = [];
  const pattern = /(`[^`]+`|\*\*[^*]+\*\*|\*[^*\s][^*]*\*|_[^_\s][^_]*_|\[[^\]]+\]\([^)\s]+\))/g;
  let last = 0;
  let match: RegExpExecArray | null;

  while ((match = pattern.exec(text))) {
    if (match.index > last) {
      runs.push({ t: 'text', text: text.slice(last, match.index) });
    }

    const token = match[0];

    if (token.startsWith('`')) {
      runs.push({ t: 'code', text: token.slice(1, -1) });
    } else if (token.startsWith('**')) {
      runs.push({ t: 'strong', runs: inline(token.slice(2, -2)) });
    } else if (token.startsWith('*') || token.startsWith('_')) {
      runs.push({ t: 'em', runs: inline(token.slice(1, -1)) });
    } else {
      const link = /^\[([^\]]+)\]\(([^)\s]+)\)$/.exec(token)!;
      runs.push({ t: /^https?:\/\//i.test(link[2]) ? 'link' : 'dead', href: link[2], runs: inline(link[1]) });
    }

    last = match.index + token.length;
  }

  if (last < text.length) {
    runs.push({ t: 'text', text: text.slice(last) });
  }

  return runs;
}

/* ---------- blocks ---------- */

export type Block =
  | { t: 'p'; runs: Run[] }
  | { t: 'h'; level: 1 | 2 | 3 | 4; runs: Run[] }
  | { t: 'hr' }
  | { t: 'pre'; text: string }
  | { t: 'list'; ordered: boolean; items: Run[][] }
  | { t: 'quote'; runs: Run[] }
  | { t: 'table'; head: Run[][]; rows: Run[][][] };

const cells = (row: string) => row.replace(/^\s*\|/, '').replace(/\|\s*$/, '').split('|').map((cell) => cell.trim());

export function markdown(source: string): Block[] {
  const blocks: Block[] = [];
  const lines = source.replace(/\r/g, '').split('\n');
  let para: string[] = [];
  let i = 0;

  const flush = () => {
    if (para.length) {
      blocks.push({ t: 'p', runs: inline(para.join(' ')) });
      para = [];
    }
  };

  while (i < lines.length) {
    const line = lines[i];
    let match: RegExpExecArray | null;

    if (/^```/.test(line)) {
      flush();
      const code: string[] = [];
      i++;

      while (i < lines.length && !/^```/.test(lines[i])) {
        code.push(lines[i]);
        i++;
      }

      i++;
      blocks.push({ t: 'pre', text: code.join('\n') });
      continue;
    }

    if ((match = /^(#{1,4})\s+(.*)$/.exec(line))) {
      flush();
      blocks.push({ t: 'h', level: match[1].length as 1 | 2 | 3 | 4, runs: inline(match[2]) });
      i++;
      continue;
    }

    if (/^\s*([-*_])\s*\1\s*\1[\s\-*_]*$/.test(line)) {
      flush();
      blocks.push({ t: 'hr' });
      i++;
      continue;
    }

    if (/^\s*\|.*\|\s*$/.test(line) && i + 1 < lines.length && /^\s*\|?\s*:?-{3,}/.test(lines[i + 1])) {
      flush();
      const head = cells(line).map(inline);
      const rows: Run[][][] = [];
      i += 2;

      while (i < lines.length && /^\s*\|.*\|\s*$/.test(lines[i])) {
        rows.push(cells(lines[i]).map(inline));
        i++;
      }

      blocks.push({ t: 'table', head, rows });
      continue;
    }

    if (/^\s*([-*+]|\d+[.)])\s+/.test(line)) {
      flush();
      const ordered = /^\s*\d/.test(line);
      const items: Run[][] = [];

      while (i < lines.length && /^\s*([-*+]|\d+[.)])\s+/.test(lines[i])) {
        items.push(inline(lines[i].replace(/^\s*([-*+]|\d+[.)])\s+/, '')));
        i++;
      }

      blocks.push({ t: 'list', ordered, items });
      continue;
    }

    if (/^>\s?/.test(line)) {
      flush();
      const quote: string[] = [];

      while (i < lines.length && /^>\s?/.test(lines[i])) {
        quote.push(lines[i].replace(/^>\s?/, ''));
        i++;
      }

      blocks.push({ t: 'quote', runs: inline(quote.join(' ')) });
      continue;
    }

    if (!line.trim()) {
      flush();
      i++;
      continue;
    }

    para.push(line.trim());
    i++;
  }

  flush();

  return blocks;
}

/* ---------- code ---------- */

/** A piece of a line of code: plain, a comment, a string, a number, a keyword or a type name. */
export type Token = { cls: '' | 'c' | 's' | 'n' | 'k' | 't'; text: string };

const KEYWORDS = /^(abstract|as|async|await|base|bool|break|case|catch|class|const|continue|def|default|do|else|enum|export|extends|false|finally|fn|for|foreach|from|function|get|if|implements|import|in|init|interface|internal|is|let|namespace|new|null|override|private|protected|public|readonly|record|return|sealed|set|static|string|struct|switch|this|throw|true|try|type|typeof|using|var|void|while|yield)$/;

export function codeTokens(text: string, lang: string): Token[] {
  const tokens: Token[] = [];
  const pattern = /(\/\/.*$|#.*$|"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|\b\d+(?:\.\d+)?\b|\b[A-Za-z_][A-Za-z0-9_]*\b)/g;
  const hashComments = /^(py|sh|yaml|yml|toml|ps1|ini)$/.test(lang);
  let last = 0;
  let match: RegExpExecArray | null;

  while ((match = pattern.exec(text))) {
    const token = match[0];
    let cls: Token['cls'] = '';

    if (token.startsWith('//') && !hashComments) {
      cls = 'c';
    } else if (token.startsWith('#')) {
      if (!hashComments) {
        continue;
      }

      cls = 'c';
    } else if (token.startsWith('"') || token.startsWith("'")) {
      cls = 's';
    } else if (/^\d/.test(token)) {
      cls = 'n';
    } else if (KEYWORDS.test(token)) {
      cls = 'k';
    } else if (/^[A-Z][a-z]/.test(token)) {
      cls = 't';
    }

    if (!cls) {
      continue;
    }

    if (match.index > last) {
      tokens.push({ cls: '', text: text.slice(last, match.index) });
    }

    tokens.push({ cls, text: token });
    last = match.index + token.length;

    if (cls === 'c') {
      break;
    }
  }

  if (last < text.length) {
    tokens.push({ cls: '', text: text.slice(last) });
  }

  return tokens.length ? tokens : [{ cls: '', text: ' ' }];
}

export function codeLines(text: string, limit?: number): string[] {
  const lines = text.split('\n');

  if (lines.length > 1 && lines[lines.length - 1] === '') {
    lines.pop();
  }

  return limit ? lines.slice(0, limit) : lines;
}

/* ---------- tables ---------- */

export function parseCsv(text: string, separator: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = '';
  let quoted = false;

  for (let i = 0; i < text.length; i++) {
    const c = text[i];

    if (quoted) {
      if (c === '"' && text[i + 1] === '"') {
        cell += '"';
        i++;
      } else if (c === '"') {
        quoted = false;
      } else {
        cell += c;
      }
    } else if (c === '"') {
      quoted = true;
    } else if (c === separator) {
      row.push(cell);
      cell = '';
    } else if (c === '\n') {
      row.push(cell);
      rows.push(row);
      row = [];
      cell = '';
    } else if (c !== '\r') {
      cell += c;
    }
  }

  if (cell || row.length) {
    row.push(cell);
    rows.push(row);
  }

  return rows;
}

/** A unified diff as [mark, text] lines, the hunk headers marked @. */
export function diffLines(patch: string): [' ' | '+' | '-' | '@', string][] {
  return patch
    .replace(/\r/g, '')
    .split('\n')
    .filter((line) => !/^(diff --git|index |--- |\+\+\+ |new file mode|deleted file mode|similarity index|rename (from|to))/.test(line))
    .map((line): [' ' | '+' | '-' | '@', string] => {
      const mark = line[0];

      return mark === '+' || mark === '-' ? [mark, line.slice(1)] : mark === '@' ? ['@', line] : [' ', line.slice(1)];
    });
}

/** What the Outbox's viewer is given for a file: its text (redacted), an image's address, or the change. */
export interface OutboxRead {
  text?: string;
  url?: string;
  width?: number;
  height?: number;
  binary?: boolean;
  diff?: [' ' | '+' | '-' | '@', string][];
  redacted?: number;
  truncated?: number;
  unchecked?: boolean;
}
