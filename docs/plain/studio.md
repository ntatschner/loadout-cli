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

The icons down the left side are the pages. *Home* shows everything that's
happening. *Sessions* shows your AI sessions. *Projects* shows the folders of code Loadout
knows about, and whether anything about them needs fixing. *Commands* lets you run anything
Loadout can do: pick it, fill in the boxes, and press **Run**. It shows you the
exact words it will type for you first.

Press **Ctrl** and **K** together to search for anything.

## Start a session

A *session* is one conversation with an AI coding agent.

1. Press **N**, or go to *Sessions* and choose **New session…**.
2. Choose your project.
3. Choose what you're about to do, or *No task*.
4. Choose **Launch**.

A box like a terminal opens. Type to the agent there.

You can close the browser. The session keeps running, because the daemon is
running it. Come back to *Sessions* to find it again.

## When something needs you

Sometimes a team of agents stops to ask you a question. You'll see it under
**Needs you** on *Home*, and in a box at the top right.

Each answer has a button. Under each button is the command it runs, in case
you'd rather type it.

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
