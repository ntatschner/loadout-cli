---
id: skill.publish-documentation
kind: skill
title: Publishing generated documentation
summary: Wiring the generated documents into CI and a site, and knowing which of them must never be published unread.
task_phrases:
  - 'publish the docs'
  - 'documentation site'
  - 'docs pipeline'
  - 'docusaurus'
  - 'mkdocs'
  - 'generate documentation in ci'
  - 'docs workflow'
---

## What this is for

`loadout docs export` writes four documents from one scan of the code, and
`loadout docs ci` writes a GitHub Actions workflow that regenerates three of
them. This is about the parts neither can do: adapting that workflow to the CI a
repository actually uses, and putting the output in front of readers.

## The one rule that matters

**Never publish the user guide unread.**

It is a scaffold. Its headings come from the shape of the code, its sections are
TODO, and it says so at the top. That marking exists so nobody mistakes it for
documentation — and a pipeline that regenerates and publishes it nightly undoes
that completely. Somebody then finds a page that reads like documentation, is
ordered by module rather than by task, and teaches them nothing.

`loadout docs ci` leaves it out by default. Keep it that way. If a guide is
wanted, write it once by hand, commit it, and let the pipeline leave it alone —
the scaffold is a starting point for a person, not an artefact.

The other three are safe to regenerate on every push. The reference and the
symbol index are derived and always true; the technical guide is the prose
already in the doc comments.

## Adapting the generated workflow

The workflow says in its first line that it is a starting point. Treat it as
one:

- **Two assumptions it makes.** Loadout has to be on `PATH`, and the project has
  to be registered, before any step runs. How that happens is the repository's
  business — a released binary, a build from source, a container — so the file
  does not decide it.
- **It writes nothing back.** No commit, no pull request, no publish. Each of
  those writes somewhere, and where is a decision about the repository rather
  than a default worth guessing. Add whichever is wanted.
- **It dates.** Action versions move and runner images change. When it breaks,
  fix the repository's copy; do not regenerate and lose whatever was adapted.

For other CI, take the export commands and drop them into whatever the
repository already uses. There is nothing GitHub-specific about them.

## Fitting a static site generator

Both Docusaurus and MkDocs consume plain Markdown, so the generated files need
no conversion. `--front-matter` adds the YAML header they read for the title and
sidebar position.

- **Docusaurus** — put the files under `docs/` and they appear. `sidebar_position`
  orders them; the guide comes first, then the architecture, then the reference.
- **MkDocs** — the same files work. Its `nav` in `mkdocs.yml` overrides the front
  matter, so either list them there or leave `nav` out.

Do not put front matter on `llms.txt`. It is read by a model rather than
rendered by a site, and its whole purpose is to say where things are in as few
tokens as possible; a YAML preamble is the opposite of that. The generated
workflow already omits it for that file.

## What to check before wiring any of it up

Run the export by hand first and read the output. Two things are worth seeing
with your own eyes:

- **How much of the reference has no prose.** It lists every public symbol,
  and on most codebases the majority carry no doc comment. That is fine for
  looking a symbol up and disappointing if you expected a manual.
- **Whether the technical guide says anything.** It carries each type's summary
  and the opening paragraph of its remarks. A codebase that documents lightly
  produces a thin guide, and no pipeline improves that — the fix is in the
  source, not in the generator.

## What not to do

- Do not hand-edit the generated files. They are overwritten on the next run.
  Anything worth keeping goes in the doc comments, where it also helps whoever
  is reading the code.
- Do not add a conversion step to produce a format nobody asked for. Markdown is
  consumed directly by every generator worth using here.
- Do not publish from a branch nobody reviews. Generated or not, it goes out
  under the project's name.
