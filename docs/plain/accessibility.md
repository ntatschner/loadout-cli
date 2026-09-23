# Accessibility

**By the end of this page:** Loadout will show and say things in the way that
suits how you read.

## What this does

You tell Loadout once how you'd like things. Then three things change:

1. How the coding agent writes to you.
2. What the coding agent shows on screen while it works.
3. What Loadout itself shows.

Loadout never guesses. Nothing changes until you turn it on.

## Before you start

- Loadout is installed. See [installing Loadout](installing.md).

## Steps

### 1. Choose a preset

A **preset** is a group of settings chosen together. Each one is named for what
it changes, not for a condition. You don't have to tell Loadout anything about
yourself.

- **screen-reader** — no moving text, simple characters, numbered menus, one
  question at a time. For people who use a **screen reader**, which is a
  program that reads the screen aloud.
- **low-vision** — fewer colours, less movement, a summary first.
- **colour-blind** — fewer colours, and never red next to green.
- **dyslexia** — short sentences, short paragraphs, numbered steps, plain words.
- **adhd** — one question at a time, a summary first, short answers, and a check
  before anything that can't be undone.
- **plain-language** — plain words, and every question explained.

### 2. Turn it on

Open a terminal. Type this, with your preset's name at the end, and press
Enter:

```sh
loadout config set accessibility-preset screen-reader
```

### 3. Check it worked

Type this and press Enter:

```sh
loadout team list
```

The first line of what you see names your preset. That's how you know it's on.

### 4. Try the launcher

Type this and press Enter:

```sh
loadout
```

With the **screen-reader** preset, you'll see a numbered list. Type a number
and press Enter to choose. This numbered list is the part people have actually
used.

### 5. Try the dashboard

Type this and press Enter:

```sh
loadout team dashboard --open
```

With the **screen-reader** or **low-vision** preset, you get a plainer page.
Every page has a **Plain view** button near the top.

### 6. Speech, only if you want it

Speech is off, even with the **screen-reader** preset. To turn it on, type this
and press Enter:

```sh
loadout config set show-speech screen-reader
```

Speech is built. It has been heard only through the Windows system voice. It
has never been heard through a screen reader. That's why it's off unless you ask.

## What has been checked

- Each preset changes what it says it changes. This has been tested.
- The dashboard has been checked by an automatic tool called axe-core. It found
  no problems.
- **No screen reader has been used with any of this.**
- **Nobody has tried it using only a keyboard and written down what happened.**

If you use a screen reader, please tell us what you heard. Open an issue on the
[Loadout project page](https://github.com/ntatschner/loadout-cli/issues).

## To turn it off

Type this and press Enter:

```sh
loadout config set accessibility-preset none
```

This clears the preset. A setting you changed on its own stays as you set it.

## If something went wrong

If the first line of `loadout team list` doesn't name your preset, check that
`LOADOUT_ACCESSIBLE` isn't set in this terminal. It's another way to choose a
preset, for one terminal only.

## What this doesn't do

- It never detects anything about you. Nothing changes until you turn it on.
- It doesn't add a font for dyslexia.
- It doesn't change the coding agent's colours.

## Next

[Setting up accessibility](../guides/accessibility.md) has every setting, when
you want more detail.
