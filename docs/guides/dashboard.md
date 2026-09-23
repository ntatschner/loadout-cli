# Using the dashboard

**At the end:** the dashboard open in your browser, and you able to find a run,
answer what it's waiting on, and stop it.

## Before you start

- Loadout installed and a project registered. See [your first run](first-run.md).
- A team run to look at, or the intention to start one. See
  [running a team](teams.md).
- A browser on the same machine.

## Steps

### 1. Start the dashboard

```sh
loadout team dashboard
```

You should see the address it's serving on printed in the terminal. It's a
loopback address — only this machine can reach it — and it carries a token that
changes every time the dashboard starts, so an old link stops working.

If a Loadout daemon is already serving a dashboard, this command prints the
daemon's address instead and starts nothing, so you don't end up with two pages
over the same runs.

A team you start from this page runs inside this command, so it stops when you
close the terminal. The page tells you so above the form.

### 2. Open the address

Copy the whole address, token included, into your browser. Or start it with
`--open` to have it opened for you:

```sh
loadout team dashboard --open
```

![The dashboard's run list. A ledger of totals across the top, then one run under "Needs you" waiting for an answer, one under "Running", and the first of two under "Finished". Each card states its run's state as a word.](../images/dashboard-list-rich.png)

You should see a band of totals across the top, then your runs grouped under
*Needs you*, *Running* and *Finished*. *Needs you* comes first because a run
waiting on you is the one most likely to be missed further down.

### 3. Choose plain or rich

Which page you get follows your accessibility profile. Under the
`screen-reader` or `low-vision` preset, or with colour set to none, you get the
plain page; otherwise the rich one. To choose for this start only:

```sh
loadout team dashboard --view plain
```

![The plain dashboard: the same four runs as a single list, each row giving the run's name, its state in words, and what it has cost.](../images/dashboard-list-plain.png)

You can also press **Plain view** in the letterhead of either page. It's early
in the tab order on purpose. `--view` doesn't override your motion setting:
asking for the rich page isn't asking for more movement.

### 4. Move between the screens

Across the top of the runs pane are six screens. The first four show the same
runs in different ways; *Waiting* shows what hasn't become a run yet, and
*Terminal* shows what a node is doing now.

- **List** — dense and complete. The default.
- **Office** — one room per run, a desk per node. The one to leave on a spare
  screen.

- **Graph** — who asked whom. Reach for it when something is stuck.

  ![The graph screen. The lead at the top, with lines to the two workers it asked for. One is marked "waiting for you".](../images/dashboard-graph.png)

- **Timeline** — where the time and money went, a strip per day.

  ![The timeline screen. Three days, each a strip showing when runs started and ended, with that day's time and cost written beside it.](../images/dashboard-timeline.png)

- **Waiting** — schedules that haven't fired and tasks nobody has finished.
  Anything held says what's holding it, in words.

  ![The waiting screen. One scheduled team run, due today at 07:00, then two open tasks. The last one says "blocked" in words, and why.](../images/dashboard-waiting.png)

- **Terminal** — a node's output, line by line, as it happens.

None of these screens can change anything. Every control lives in the detail
pane, so answering a gate is built once rather than several times.

### 5. Open a run and answer a gate

Select a run. It opens in the detail pane.

![A run open in the detail pane. Its three done-when criteria, two marked met and one not attempted, and a gate asking whether to open a pull request, with Approve and Refuse buttons.](../images/dashboard-run-detail.png)

You should see its done-when criteria, each marked met, unmet or not attempted
in words, and any gate it's waiting on. A gate is a point where the run stops
and asks you before going on. Press **Approve** or **Refuse**.

The button runs the same command you could type yourself:

```sh
loadout team gate
```

That prints what the run is waiting on and how to answer it.

### 6. Start a team from the page

Press the button to start a team.

![The form for starting a team run. A team chosen, the goal typed in, and three done-when criteria, one per line.](../images/dashboard-start-form.png)

Choose a team, type the goal in your own words, and put one done-when criterion
per line. Criteria are one per line rather than comma-separated because a
criterion is a sentence, and sentences contain commas.

### 7. Stop or hold a run

From the detail pane, stop or hold the run. From a terminal it's:

```sh
loadout team halt --pause    # hold it before its next round
loadout team halt --resume
loadout team halt            # stop it after the round it is in
```

### 8. Change the look, if you want to

Open the settings page.

![The dashboard settings. The High contrast theme is selected, and the page is shown in black and white with every rule drawn.](../images/dashboard-themes.png)

There are four themes, each with a light and a dark version, and a set of
accents that were each measured against their theme. These settings stay in
this browser and nowhere else.

## Only watching

For a screen in a corner, start it so nothing on the page can change a run:

```sh
loadout team dashboard --watch-only
```

The server refuses anything that would start or change a run, and the page puts
its controls away rather than showing buttons that do nothing.

## If it went wrong

- **The page says the token is wrong.** The dashboard was restarted; copy the
  new address from the terminal.
- **The buttons are missing.** It was started with `--watch-only`.
- **The page is plain when you expected rich.** Your profile chose it. Use
  `--view rich` or the **Plain view** button.

## What this doesn't do

- It fetches nothing from the internet, so there's no webfont and no image.
  Office desks are squares with names written in them, because Loadout ships no
  art.
- Only the dashboard's own machine can reach it by default.
- No screen reader has been used with it, and no keyboard-only pass by a person
  has been recorded. [Setting up accessibility](accessibility.md) has what was
  checked.

## Next

[Running a team](teams.md)
