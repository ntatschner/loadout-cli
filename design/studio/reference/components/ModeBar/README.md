# ModeBar

ModeBar sits at the top of every mode. It holds the ModeSwitcher, search (Ctrl K), and **what is waiting on you**: "2 waiting on you · Push the tag v0.66.0 to origin?". Pressing that goes to the oldest question, whichever mode you are in, so a focus mode never hides a question.

- Nothing waiting reads "nothing waiting on you", in muted mono.
- With `list`, pressing the count opens the waiting list (every item, oldest first, each with its main action) instead of going to the oldest: the page draws it from `onWaiting(first, button)`, anchored to the button. The button says it has a popup and, through `setOpen`, whether it is open. `setWaiting(list)` changes the count in place, so an open list keeps its anchor and what it said.
- In Minimal it is the only chrome on the page: compact switcher, search, and the waiting count.
- It has `view-transition-name: ls-modebar`, so it holds still while the page changes mode beneath it.
- Below 600px the switcher goes compact and the waiting label shortens to its count.
