# OfficePanel

What opens when you pick a room or a person in the office, by click or by Enter. It replaces the classic popup with a Studio panel beside the picture. It has the **same actions**, as Studio buttons that show their exact commands. Anything that can't be undone asks first, by name, in a Dialog.

## What each one shows

| Picked | Shows | Actions |
|---|---|---|
| A person | who they are, their role, what they're doing | Open the run |
| The lead's office | the run's goal, what it has spent of its budget, how it ended | Open the run; **Stop the run** (`loadout team halt <run>`), asking first; **Name this room** |
| A team's desks | who is working | Open the run |
| Meeting room | what is waiting for you; it's answered on the run's page | Open the run |
| Kitchen, lounge | who is taking a break | none |
| The waiting room, What is scheduled next | what's waiting and what will fire | **Stop scheduling it** (`loadout team schedule remove <name>`), asking first |
| Reception | who has just arrived | Open the run |
| Mail room, a pigeonhole; Storage, a shelf | what the runs left | Open the run, when it's one run's |
| The garbage room | runs and teams in the bin | **Bring it back** (`loadout team runs restore <run>`, or `loadout team restore <team>`); **Delete for good** (`loadout team bin empty <name> --yes`), asking first |
| The server room | the daemon and its schedules | **Hold the schedules** (`loadout daemon pause`) or **Resume the schedules** (`loadout daemon resume`); **Restart the daemon** (`loadout daemon restart`), asking first |

A room the floor shares shows each team's part of it under the team's name, with **Open** for each.

## Name this room

A run's own room (the lead's office) has **Name this room**: a box and **Name it**, with the command under it:

- with a name: `loadout team name <run> --room "<name>"`;
- emptied: `loadout team name <run> --clear`, which puts the worked-out name back.

## Accessibility

- It's a region named for what was picked, and its heading takes focus when it opens.
- Close (×) and Escape both close it and return to the picture, where the ring was.
- What a button did is said in a status line under the actions.
