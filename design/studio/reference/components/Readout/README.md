# Readout

What a reading command says, drawn as a view on the page. It replaces drawers whose button was "Show" and whose answer was the command's printed text: usage, instructions stats, probe, docs find, style show, voice runs and time, team capabilities, blocked, tools health and audit, doctor, drift, repo check, project survey and discover, checkpoint and backup lists, commands.

## What it shows

- A **title**, when it was read, and **Read it again**.
- **Small settings** that change what it reads ("Over the last 30 days"), as fields. Changing one updates the line, and Read it again reads with them.
- **The view**, from the command's `--json`:
  - **figures:** a few large numbers with labels;
  - **table:** rows and columns, with numbers right-aligned and tabular. It scrolls inside itself when wide;
  - **document:** Markdown, for a style, a guide or a handoff;
  - **findings:** a HealthList, worst first;
  - **list:** names with a line each.
- **Read with:** the exact line, with `--json`.

## Do and don't

- Do read with `--json` and draw the answer. A command that has no JSON yet gets it added (the brief lists which).
- Do read on open when it's cheap. When it's slow (probe, survey, drift across every project), wait for **Read it** (`lazy`).
- Don't show a transcript. If a command's text is the answer (a style, a guide), it's a document.
