# Accessibility

*Want the steps in order? [Setting up accessibility](guides/accessibility.md) walks through it, and [accessibility in plain words](plain/accessibility.md) says it in shorter sentences.*

Loadout can be told how you want to be written to and asked. You set it once,
and it changes three things: how the agent you launch talks to you, what that
agent draws while it works, and what Loadout itself prints.

```bash
loadout config set accessibility-preset screen-reader
```

Nothing is detected. You turn it on, and the first line of output says which
profile is active, so you can see it took.

## Turning it on

Three places, and the nearest one wins:

1. `loadout team list --accessible` for one command, or
   `--accessible=dyslexia` to name a profile.
2. `LOADOUT_ACCESSIBLE=screen-reader` for a shell. `1`, `true` and `yes` mean
   the screen-reader profile; `0`, `false` and `none` mean off.
3. `loadout config set accessibility-preset <name>` for good.

`NO_COLOR` is obeyed whatever the profile says, and switches off every escape
sequence rather than colour alone, because bold is an escape too.

## The presets

A preset is a starting point, not a mode. It sets a bundle of settings, and
anything you set yourself wins over it — so you can take the dyslexia bundle
and still ask for the field's own vocabulary.

| Preset | What it sets |
|---|---|
| `screen-reader` | No redraws, ASCII glyphs, lists instead of tables, numbered menus, the text launcher, no motion, the terminal bell, one question at a time |
| `low-vision` | Sixteen colours, reduced motion, bold-only emphasis, a summary first |
| `colour-blind` | Sixteen colours, and no red-and-green pairs |
| `dyslexia` | Sentences under twenty words, paragraphs of four, bold-only emphasis, one word per thing, numbered steps, plain language, a summary first |
| `adhd` | One question at a time with "question 2 of 4", a summary first, concise answers, confirmation before anything irreversible, reduced motion, no redraws |
| `plain-language` | Plain words, every question explained, one word per thing, and why it is being asked |

The settings are named for what they reduce rather than for a condition. You
should not have to declare a diagnosis to a configuration file to get shorter
sentences, and most of these help people who would not claim one.

## What you can change on its own

Every setting is settable by itself. `loadout config list` shows them under
Accessibility, Writing and Display.

**How a question arrives** — `ask-style` (choices, written or mixed),
`ask-one-at-a-time`, `ask-why`, `ask-explain` (on-request, always or never),
`ask-recommend`, `ask-unsure`, `ask-progress`, `ask-before-risky`.

**What the prose looks like** — `write-verbosity` (concise, standard or full),
`write-technicality` (plain, mixed or technical), `write-summary-first`,
`write-sentence-words`, `write-paragraph`, `write-steps`, `write-emphasis`,
`write-same-word`, `write-bionic`.

**What gets drawn** — `show-colour` (full, sixteen or none), `show-colour-safe`,
`show-glyphs` (unicode or ascii), `show-motion`, `show-redraw`, `show-tables`
(tables or lists), `show-menus` (arrows or numbered), `show-launcher` (full or
text), `show-bell`.

## What it changes

### What the agent writes

A section at the end of the compiled context says how you asked to be written
to: one question per message with a sentence on why, a way out that counts as
an answer, a recommended option labelled with its downside and never chosen for
you, the answer in the first sentence, sentences and paragraphs within your
limits, numbered steps, bold and nothing else.

It applies to what an agent writes and never to what it quotes. A log line, a
diff or an error is reproduced exactly. And it changes how things are said,
never what is done: a change is still tested and a failing test still reported
as failing, with its output.

You can move between the levels mid-session by saying so — "be brief", "say
more", "less technical", "explain that more simply" — and the agent changes for
the rest of the session and says that it has.

### What the agent draws

Claude Code is started in its own screen-reader mode where you have asked for
no redraws, with reduced motion, spinner tips off, and the terminal bell as its
notification channel where you asked for a bell. Codex has its animations
switched off; it has no screen-reader mode, and Loadout says so at launch
rather than leaving you to notice.

Neither agent's theme is touched. Both ship colour-blind-safe themes in a light
and a dark variant, and which one you want is not knowable from a profile —
picking one would flip the colours of a terminal you have already set up.

### What Loadout prints

- Colour restricted to the sixteen your own terminal theme can remap, or none.
- ASCII where you asked for it: no box drawing, no braille, no arrows. An em
  dash becomes a hyphen, an ellipsis three dots.
- Tables become one line per value, each carrying its own heading.
- Menus become numbered lists answered by number, including the ones that take
  several answers.
- Spinners and progress bars stop redrawing.
- The terminal bell rings when an answer is wanted, and at no other time.
- `loadout` with no arguments opens a text menu of the same commands instead of
  the full-screen launcher.

The full-screen launcher, for somebody who can see it and needs it drawn
differently: colour comes down to the sixteen your own terminal theme defines,
the boxes go where you asked for ASCII, and the opening animation does not
play where you asked for less movement.

### The dashboard

`loadout team dashboard` serves a page showing what the team runs are doing. It
was built to WCAG 2.2 AA from its first commit rather than fixed afterwards: a
live region present from the first paint, every state written as a word and not
only as a colour, real headings and lists, a skip link, focus outlines that
survive a forced-colours mode, targets no smaller than 40 pixels, and both
`prefers-reduced-motion` and `forced-colors` honoured. Following a run moves
focus to the heading of what you asked for.

**axe-core finds nothing wrong with it.** Version 4.10.2, against WCAG 2.0,
2.1 and 2.2 at A and AA plus its best-practice rules. Eleven states have been
audited as the page grew — the run list, a run open at each of its four depths,
the office, the graph, the timeline, the roles table, a brief offered for
changing, and the form for starting a team — and every one comes back with **no
violations and nothing left incomplete**, with 37 to 50 rules passing depending
on what is on screen.

It has caught one thing nobody would have. The controls for steering a live
node came out twenty-one pixels tall, under the twenty-four the standard asks
for, because they were relying on their own padding where the buttons in the
run list have carried a minimum since the first commit. Every control in that
row now carries one. The rules that pass are the ones the paragraph
above claims — `color-contrast`, `target-size`, `skip-link`, `bypass`,
`landmark-one-main`, `heading-order`, `region`, `list`, `listitem`,
`page-has-heading-one`. Following a run was confirmed to move focus to the
heading of what was asked for, and the live region is `role="status"` with
`aria-live="polite"`.

The office and the graph were built elements-first for this reason. Every desk
is a button whose accessible name reads as a sentence — "implementer/1,
implementer, working, dotnet test" — and the sprite beside it is marked
decorative and is empty until there is art to put in it. Tab order through a
room follows the team: the lead, then its workers. Every box in the graph is a
focusable link carrying name, role, state and spend, and a node the run is
waiting on says "waiting for you" in its name as well as being drawn with a
heavier border.

The audit ran against a copy of the page served without its content security
policy, because that policy says `connect-src 'self'` and correctly stops a
browser pulling axe-core in from anywhere. The markup is the same file the
server embeds and the data was stubbed so every part of it rendered; axe reads
the DOM rather than the response headers.

#### Both pages, audited 20 September 2026

There are two presentations now — the plain page, and the full one somebody
gets when their profile says nothing about reading a screen. Both were audited
with axe-core 4.10.2 against the same rule set, against the page's own markup
and against the answers the real server gave, recorded from a live dashboard
rather than stubbed.

**No violations, in any state, on either page.** Eleven states of the full page
— the run list, each of the other five destinations, the settings page, and a
run open at each of its four depths — and four of the plain one, with 38 to 51
rules passing depending on what is on screen.

It found two things on the full page, both fixed:

- The wordmark at the head of the drawer sat in a plain `div`, outside every
  landmark. Somebody moving between regions stepped over it. It is inside the
  navigation it labels now — one landmark rather than two, and no nesting.
- The skip link points at the runs heading, and the settings page puts that
  heading away, so the first control on the page aimed at something hidden.
  For the one person most likely to use a skip link, that is a link that goes
  nowhere. It follows the page now and says where it is going.

And it declined to judge two things, which is not the same as finding them
wrong:

- The paper grain used to be a `background-image` on the body, and axe cannot
  compute a contrast ratio against an image, so it left the two headings that
  sit straight on the page undecided. The grain moved to a layer behind the
  content; the body is a flat colour that can be measured, and it looks the
  same. Those checks pass now.
- The office and the graph still come back incomplete — 140 contrast checks
  over rooms with art painted behind them, two overlapping desk targets, and
  24 pieces of SVG text in the graph. These are the same on the plain page, so
  they are not something the full one introduced, and they are all "axe cannot
  work this out" rather than "this is wrong". Every desk measured at or above
  the target size when checked directly.

The themes were measured separately, by hand, across all 48 combinations of
four themes, two schemes and six accents: every piece of text passes, the worst
at 5.2:1 against the 4.5:1 required.

It has still not been heard with a screen reader, and no keyboard-only pass by
a person has been recorded. Nor has it been rendered at phone width: the
automation would not give up control of the viewport, so what is proven is that
the three-column rule lives inside a `min-width` query and nowhere else, which
is the behaviour that matters and is not the same as having looked at it.

### The dashboard speaking

The page asks the machine what it can say, because a browser cannot tell and
should not try: there is no API for detecting a screen reader, every heuristic
that claims to is wrong often, and the ones that work at all work by
fingerprinting somebody because of a disability. The machine can tell, and
already does — it is the same channel the launcher uses.

Four conditions, every one required: a screen reader answered rather than a
system voice; `show-speech` is `screen-reader`; the browser is on that machine,
checked per request rather than per listener; and the request carries the
token. The settings page says which of the four is missing rather than being
quietly silent about it.

Nothing is said that is not also written on the page, and a line over 400
characters is cut.

**Verified only against a stub.** There is no screen reader here, so the path
where one answers has never run for real. What the stub proves is the order of
the refusals — four mutations, each removing one condition and each failing its
own test, which is the part that decides whether somebody who never asked ends
up with a talking computer. Three earlier mutation attempts broke the build
instead of the behaviour and proved nothing; they are not counted.

### The launcher speaking

```sh
loadout config set show-speech screen-reader
```

Off by default, and **off even under the `screen-reader` preset**, which keeps
giving you the text launcher instead. The text launcher is the answer people
have actually used; this one has been heard by nobody, and offering it by
default would be offering an unverified experience to exactly the people who
cannot check it.

With it on, the full-screen launcher says each row as the cursor lands on it —
the project's name first, because you hear the first word and interrupt as soon
as it is the wrong row, then whether it is on this machine. What the columns
say, and nothing that only the colours say.

It speaks to a screen reader that is already running, and to the system's own
voice when none is. Loadout never starts one. On Windows that means NVDA if it
answers and the Windows voice otherwise; on macOS `say`; on other Unixes
`spd-say`, which is speech-dispatcher, which is what Orca is already using — so
it reaches the same voice rather than starting a second.

**Only the Windows system voice is verified**, and only as far as "the route
answers": this machine has two voices installed and the COM object is reachable.
The NVDA path is written from its documented entry points and has never
answered, because there is no NVDA here. Neither Unix route has been heard.
JAWS has an equivalent route and is deliberately not claimed.

## Bionic formatting

`write-bionic` bolds the first half of each word in the agent's own prose. It
is off by default and offered with its evidence: every controlled study of this
finds no gain in reading speed or comprehension, and one finds it slower. It is
here because people preferred styled text even where it did not help them, and
a preference you can switch off is a fair thing to offer.

It is refused under the screen-reader profile, where bold markup is read aloud
as emphasis on every word.

## What Loadout does not do

**No dyslexia font.** Every peer-reviewed study of dyslexia-specific fonts finds
no gain in speed or accuracy, and preference does not predict performance. The
font is your terminal's to choose in any case.

**No readability score.** A score measures sentence length, not whether anybody
understood. The limits above are the ones GOV.UK and digital.gov use, and the
test is people reading it.

**No detection.** Nothing here is turned on by guessing at your needs from your
machine.

## What has been verified, and what has not

Said plainly, because a claim about accessibility that nobody checked is worse
than no claim.

**Tested:** that each profile produces the guidance, flags and settings it
promises; that a build of an agent without a flag says so instead of going
quiet; that accessible output carries no escape sequences; that menus are
numbered, bounded and refused when the number is outside the list; that a table
becomes labelled lines with long values unbroken.

**Not tested with a screen reader.** Nobody on the machine this was built on
runs one. That NVDA, JAWS, VoiceOver or Orca read this well is drawn from
published guidance and from what other command-line tools have shipped, not
from listening to it. If you use one, what you find is worth more than any of
the above, and an issue saying what you heard is the most useful thing you
could send.

**Built, and heard only through the Windows system voice, never through a
screen reader:** speaking to a running screen reader, from the full-screen
launcher and from the dashboard. Both are described above, under
[the launcher speaking](#the-launcher-speaking) and
[the dashboard speaking](#the-dashboard-speaking), and both are off unless you
set `show-speech`.

## The rest

- [First run and configuration](first-run.md) — where `config.yaml` lives
- [The launcher](launcher.md) — the full-screen one, and its keys
- [Commands](commands.md) — everything `loadout` can do
