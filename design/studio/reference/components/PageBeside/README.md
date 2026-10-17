# PageBeside

In **Terminal Focus** and **Team Focus**, the six pages (Ideas, Projects, Context, Settings, Memory and Spend) don't take the screen. They open **beside** what you are watching: your terminals, or the runs board. These stay live and can still be typed into and answered.

- The panel's head says which page it is and what it is beside: "Spend · beside your terminals". The page's own head (its title and pickers) is inside it.
- **Close** (or Escape) closes it and puts back the route you came from. Escape is ignored while you are typing in a field, inside a terminal, or in a dialog, so it never steals a key from an agent.
- The panel is 52% of the width, at least 460px, sticky, and scrolls on its own. Inside it, every page lays out as one column: the panel is a size container named `page` (as is a WorkDesk view in Pure Focus), and the pages switch on its width, not the window's.
- Below 1100px the page takes the width, and closing it gives the terminals or the board back.
- It slides in from the right over 450ms on the travel ease. Under reduced motion it fades; under none, and in Pure Focus, it just appears.

`BesideGrid(focus, beside)` puts the two side by side.
