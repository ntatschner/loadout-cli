# IdeaStages

IdeaStages draws the five stages an idea passes through, as `loadout idea list` names them: **Captured, Answering, Ready, Proposed, Accepted**. The stages behind it are filled, the one it is at has a ring, and the idea's state word (round running, waiting for you, last round failed, shaping) sits under it.

- An ordered list with `aria-current="step"` on the stage it is at.
- The ring takes Needs for "waiting for you" and Bad for "last round failed"; the word is always written.
- Below 600px the names give way to the dots and one line under them: "Stage 4 of 5: Proposed · waiting for you".
