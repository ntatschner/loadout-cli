# ModeBar

ModeBar sits at the top of every mode. It holds the ModeSwitcher, search (Ctrl K), and **what is waiting on you**: "2 need you · Push the tag v0.66.0 to origin?". Pressing that goes to the oldest question, whichever mode you are in, so a focus mode never hides a question.

- Nothing waiting reads "nothing waiting on you", in muted mono.
- In Minimal it is the only chrome on the page: compact switcher, search, and the waiting count.
- It has `view-transition-name: ls-modebar`, so it holds still while the page changes mode beneath it.
- Below 600px the switcher goes compact and the waiting label shortens to its count.
