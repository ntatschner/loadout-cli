# Outbox

Outbox is **What it delivered**, the fifth depth of a run (key 5): every file the run left behind, in a tree beside a viewer. It reads only: browse, preview, see the change, copy the path, download one file or everything. Nothing in it changes anything, so nothing in it needs a command, but each file shows the terminal line that reads it.

## Where its list comes from

`loadout team outbox <run> --json`, which Studio can already read:

- `files`: every file the run's commits touched (path, added, changed or removed, the commit, the node that delivered it).
- `loose`: files a node reported that no commit carries, such as a planner's `PLAN.md`, with their size, or `null` when nothing is there now ("gone").
- `missing`: commits a node reported that the repository no longer has. They are named, not dropped.
- `repository`: where the run worked. When every node had its own worktree and they have been cleared away, it cannot be told, and the Outbox says so instead of showing an empty list.

## The tree

- **By node** (the default): node, then commit, then files; uncommitted files under "not committed · on disk". **By folder**: the repository's own folders.
- Each file carries its change in a word chip: added (Good edge), changed, removed (Bad edge), a loose file's kind (Needs edge: reported, not committed) and gone (struck through). The word always says it; the edge only repeats it.
- Find a file filters as you type and opens every group that holds a match.
- A real ARIA tree: Up and Down move, Right opens or steps in, Left closes or steps out, Home and End, Enter or Space opens a file.

## The viewer

| The file is | It shows |
|---|---|
| Markdown | Rendered in Studio's type, with Source to see it as written. Built as nodes, never as HTML from the file; only `http` and `https` links are live, others are shown as text |
| Code and plain text | Line numbers, in the terminal palette (`term-*`), already measured at 4.5:1 (7:1 in high contrast). Ligatures off |
| CSV, TSV | A table, numbers right-aligned, with its row and column count |
| JSON | Pretty-printed as code; shown as written if it does not parse |
| An image (png, jpg, gif, webp, svg) | On a checkerboard, with its size. An SVG is shown as an `img`, never inline, so its scripts never run |
| Binary, or over the size limit | Says so and offers Download; a large text file shows its first 2,000 lines and says how many are not shown |
| Removed | How it was just before the commit that removed it |
| Gone (a loose file) | That the node reported it and it has since gone |

- **The file** shows it as its commit left it; **The change** shows that file's part of the node's patch.
- Everything is redacted on the way out, as the papers and the patch are, and the viewer says how many lines were taken out. A file that cannot be checked in time is not shown; Download still works.
- Download gets one file as its commit left it (a loose file as it is on disk now). **Download all** gets `<run>-outbox.zip`: every delivered file at its commit, plus the loose files that are still there.
- Copy path copies the full path in the repository.

## Proposed daemon routes (not built yet)

Studio can list the outbox today. Reading a file's contents needs routes the daemon does not have yet. Each is a read, so each goes in the `Reads` list beside `documents` and `diff`:

- `GET /api/runs/<run>/file?commit=<sha>&path=<path>` and `?loose=<path>`: the file's text (redacted) or bytes. Only a path the run's outbox lists may be read, matched exactly before it is joined to anything, so nothing can climb out of the repository. Answers with its type, its size, how many lines were redacted, and whether it was cut short.
- `GET /api/runs/<run>/outbox.zip`: Download all.
- The change for one file comes from the existing `/api/runs/<run>/diff/<node>`, filtered to that path.

## On a phone

Below 700px of its own width, the tree and the viewer take turns: picking a file opens the viewer, and "The files" goes back to the tree with focus on the file you were reading.

## Accessibility

- The tree is `role="tree"` with levels, expanded state and selection. The viewer's body is a focusable region, so long files scroll by keyboard.
- A refusal or a status (copied, downloading) is said in words in a live status.
