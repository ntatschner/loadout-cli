# Studio, in plain words

Studio is a page in your web browser where you can work with Loadout. You can
start an AI session there, type to it, and see everything that's going on.

It's new, so it's called a *preview*. The older page, the dashboard, still
works the same way.

## What you need first

- Loadout installed. See [installing Loadout](installing.md).
- One project added. See [your first launch](first-launch.md).
- The Loadout *daemon* running. The daemon is a program that keeps running in
  the background, so your sessions keep going when you close the browser. Start
  it by typing this into a terminal and pressing Enter:

  ```sh
  loadout daemon
  ```

## Open Studio

1. Open a terminal.
2. Type this and press Enter:

   ```sh
   loadout dashboard --open --studio
   ```

3. Your browser opens on Studio's *Home* page.

This also signs your browser in. Signing in means Studio knows this browser is
allowed in. It lasts 90 days.

If Studio ever says *This browser isn't signed in*, do step 2 again.

## Find your way round

The icons down the left side are the pages:

- *Home* shows everything that's happening.
- *Sessions* shows your AI sessions.
- *Ideas* is where you answer an AI's questions about something you want to
  build, and pick from its plan.
- *Teams* shows groups of AI agents working together, and lets you answer when
  they stop to ask you something.
- *Memory* holds the notes Loadout keeps about each project. You can read and
  change them.
- *Context* shows the notes an AI will be given before it starts, and how big
  they are.
- *Spend* shows how much your AI sessions have used.
- *Projects* shows the folders of code Loadout knows about, and whether anything
  about them needs fixing.
- *Commands* lets you run anything Loadout can do. Pick it, fill in the boxes,
  and press **Run**. It shows you the exact words it will type for you first.

Press **Ctrl** and **K** together to search for anything.

## Change how Studio is laid out

Along the top are five buttons called *modes*. Each one lays out the whole of
Studio for a different job. Press **Ctrl**, **Shift** and **M** together to move
to the next one. Your browser remembers which you picked.

- *Default* is Studio as this page describes it.
- *Terminals* shows your sessions as tabs, with up to four on the screen at
  once, so you can watch and type into several.
- *Teams* shows every team's run as one line on a board. You can answer a
  question in its line.
- *Minimal* shows one quiet list of what's happening. Pick one and it fills the
  screen. Press **Esc** to go back.
- *Pure* puts a list, the thing you picked, and its buttons side by side. Every
  button has a number, and pressing the number does the same as clicking it.
  Press **?** to see every key.

Whichever you pick, the top right of the screen says how many things are
waiting for you. Press it to go to the oldest.

In Pure, a button that can't be undone, like stopping a run, asks you to press
its number twice. Press **Esc** if you change your mind.

## Start a session

A *session* is one conversation with an AI coding agent.

1. Press **N**, or go to *Sessions* and choose **New session…**.
2. Choose your project.
3. Choose what you're about to do, or *No task*.
4. Choose **Launch**.

A box like a terminal opens. Type to the agent there. To move out of the box
with the keyboard, press **Ctrl** and **]** together.

Under the box, Studio says in words when the agent is waiting for you, or
asking you to allow something. You answer it by typing in the box. Studio
never answers for you.

You can close the browser. The session keeps running, because the daemon is
running it. Come back to *Sessions* to find it again.

## When something needs you

Sometimes a team of agents stops to ask you a question. You'll see it under
**Needs you** on *Home*, and in a box at the top right.

Each answer has a button. Under each button is the command it runs, in case
you'd rather type it.

On the *Teams* page, **Needs you** lists every reason a team wants you. Each one
goes away by itself once it's dealt with.

Some questions have a box. If a team is about to give an agent its
instructions, you can change them in the box before they're sent.

## A team's run

Choose a run on the *Teams* page to see it. Press **1** to **5** to see:

1. what it said
2. what it cost
3. the instructions each agent was given, and what it said back
4. what it changed in the code
5. the files it made, which you can read and download

Nothing on that page can be changed by accident: anything that changes
something asks you first, or shows you the command it runs.

## Make it easier to read

Go to **Settings**, then **Appearance**. You can choose:

- dark or light, and high-contrast versions of each
- a colour for the highlights
- how much space there is between things
- how much moves on the page

If your computer is set to reduce motion, Studio always follows that.

## Words used here

- **Session** — one conversation with an AI coding agent.
- **Daemon** — a Loadout program that runs in the background.
- **Team run** — several agents working on one goal together.
- **Signed in** — Studio knows this browser is allowed in.
